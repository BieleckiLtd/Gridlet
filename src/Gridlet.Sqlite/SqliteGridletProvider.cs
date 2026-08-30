using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>The SQLite implementation of the Gridlet provider boundary.</summary>
public sealed class SqliteGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider, IForeignKeyLookupProvider,
    ITableImportProvider, IColumnDistinctValuesProvider
{
    public GridletProviderNames ProviderName => GridletProviderNames.Sqlite;

    public GridletProviderCapabilities Capabilities { get; } = new(
        DefaultSchema: SqliteIdentifier.MainSchema,
        SupportsSchemas: false,
        SupportsViews: true,
        SupportsStoredProcedures: false,
        SupportsFunctions: false,
        SupportsTriggers: true,
        SupportsClusteredPrimaryKeys: false,
        SuggestedDataTypes:
        [
            "INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC", "BOOLEAN", "DATE", "DATETIME",
            "VARCHAR(50)", "VARCHAR(100)", "DECIMAL(18,2)",
        ],
        SelectExample: "SELECT * FROM {object} LIMIT 100;",
        CreateTriggerExample:
            "CREATE TRIGGER [main].[NewTrigger]\nAFTER INSERT ON [SomeTable]\nBEGIN\n    SELECT 1;\nEND;",
        ObjectEditMode: "Recreate",
        SupportsCheckConstraints: true,
        SupportsUniqueConstraints: true,
        SupportsIndexes: true,
        SupportsSessions: true,
        SupportsQueryPlans: true,
        SupportedTableOptions: [SqliteTableOptions.WithoutRowId, SqliteTableOptions.Strict],
        SupportsImport: true);

    public ISchemaReader Schema { get; } = new SqliteSchemaReader();

    public ITableDataService Data { get; } = new SqliteTableDataService();

    public IQueryRunner Query { get; } = new SqliteQueryRunner();

    public ITableWriteService Writes { get; } = new SqliteTableWriteService();

    public ITableDdlService Ddl { get; } = new SqliteTableDdlService();

    public Task<TableImportResult> ImportAsync(
        GridletConnectionContext context, string schema, string table, TableImport import,
        CancellationToken cancellationToken = default)
        => SqliteTableImportService.ImportAsync(context, schema, table, import, cancellationToken);

    public async Task<GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return new GridletDatabaseSystemInfo("SQLite", connection.ServerVersion);
    }

    public async Task<IReadOnlyList<ForeignKeyLookupItem>> LookupForeignKeyAsync(
        GridletConnectionContext context, string schema, string table, string keyColumn,
        string labelColumn, IReadOnlyList<object?> keys, string? search, int limit,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        var key = SqliteIdentifier.Quote(keyColumn);
        var label = SqliteIdentifier.Quote(labelColumn);
        var predicates = new List<string>();
        var keyParameters = new List<string>();
        foreach (var value in keys.Where(value => value is not null).Distinct().Take(limit))
        {
            var parameter = $"@key{keyParameters.Count}";
            keyParameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, value!);
        }
        if (keyParameters.Count > 0)
        {
            predicates.Add($"{key} IN ({string.Join(", ", keyParameters)})");
        }

        var trimmed = search?.Trim();
        var order = key;
        if (!string.IsNullOrEmpty(trimmed))
        {
            command.Parameters.AddWithValue("@search", trimmed);
            var searchPredicates = new List<string> { $"CAST({key} AS TEXT) = @search" };
            if (trimmed.Length >= 2)
            {
                command.Parameters.AddWithValue("@contains", $"%{EscapeLike(trimmed)}%");
                searchPredicates.Add($"CAST({label} AS TEXT) LIKE @contains ESCAPE '\\'");
            }
            predicates.Add($"({string.Join(" OR ", searchPredicates)})");
            order = $"CASE WHEN CAST({key} AS TEXT) = @search THEN 0 " +
                $"WHEN CAST({label} AS TEXT) = @search THEN 1 " +
                $"WHEN CAST({label} AS TEXT) LIKE @search || '%' THEN 2 ELSE 3 END, {label}, {key}";
        }

        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 50));
        command.CommandText = $"SELECT {key}, {label} FROM " +
            $"{SqliteIdentifier.QuoteQualified(schema, table)}" +
            (predicates.Count > 0 ? $" WHERE {string.Join(" OR ", predicates)}" : "") + " " +
            $"ORDER BY {order} LIMIT @limit;";
        var items = new List<ForeignKeyLookupItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ForeignKeyLookupItem(
                reader.IsDBNull(0) ? null : reader.GetValue(0),
                reader.IsDBNull(1) ? null : reader.GetValue(1)));
        }
        return items;
    }

    public async Task<IReadOnlyList<object?>> GetDistinctColumnValuesAsync(
        GridletConnectionContext context, string schema, string table, string column,
        string? search, int limit, CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        var quotedColumn = SqliteIdentifier.Quote(column);
        var qualified = SqliteIdentifier.QuoteQualified(schema, table);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);

        string? columnType = null;
        // Validate existence to give a clear 400 rather than a cryptic SQLite error and capture type.
        await using (var validate = connection.CreateCommand())
        {
            validate.CommandText = "SELECT type FROM pragma_table_info(@table, @schema) WHERE name = @col LIMIT 1;";
            validate.Parameters.AddWithValue("@table", table);
            validate.Parameters.AddWithValue("@schema", schema);
            validate.Parameters.AddWithValue("@col", column);
            var typeObj = await validate.ExecuteScalarAsync(cancellationToken);
            if (typeObj is null || typeObj is DBNull) throw new GridletValidationException($"Column '{column}' does not exist on {qualified}.");
            columnType = Convert.ToString(typeObj);
        }

        var capped = Math.Clamp(limit, 1, 50);
        var trimmed = search?.Trim();
        var isDateByName = column.Contains("date", StringComparison.OrdinalIgnoreCase)
            || column.Contains("time", StringComparison.OrdinalIgnoreCase)
            || column.Contains("atutc", StringComparison.OrdinalIgnoreCase);
        var isDistribution = string.IsNullOrEmpty(trimmed) && capped <= 10
            && columnType is not null
            && (columnType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("DOUBLE", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
                || isDateByName);
        if (isDistribution)
        {
            long distinctCount;
            await using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = $"SELECT COUNT(DISTINCT {quotedColumn}) FROM {qualified} WHERE {quotedColumn} IS NOT NULL;";
                distinctCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));
            }
            if (distinctCount <= capped)
            {
                await using var allCmd = connection.CreateCommand();
                allCmd.CommandText = $"SELECT DISTINCT {quotedColumn} FROM {qualified} WHERE {quotedColumn} IS NOT NULL ORDER BY {quotedColumn};";
                var allValues = new List<object?>();
                await using var r = await allCmd.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken)) allValues.Add(r.IsDBNull(0) ? null : SqliteValues.Materialize(r.GetValue(0)));
                return allValues;
            }
            var step = Math.Max(1, distinctCount / capped);
            var sampled = new List<object?>();
            for (var i = 0; i < capped; i++)
            {
                var offset = i * step;
                await using var offCmd = connection.CreateCommand();
                offCmd.CommandText = $"SELECT {quotedColumn} FROM (SELECT DISTINCT {quotedColumn} AS v FROM {qualified} WHERE {quotedColumn} IS NOT NULL ORDER BY v) LIMIT 1 OFFSET @off;";
                offCmd.Parameters.AddWithValue("@off", offset);
                var val = await offCmd.ExecuteScalarAsync(cancellationToken);
                if (val is not null && val is not DBNull) sampled.Add(SqliteValues.Materialize(val));
            }
            return sampled.Distinct().ToArray();
        }

        await using var command = connection.CreateCommand();
        if (string.IsNullOrEmpty(trimmed))
        {
            command.CommandText = $"SELECT DISTINCT {quotedColumn} FROM {qualified} WHERE {quotedColumn} IS NOT NULL ORDER BY {quotedColumn} LIMIT @limit;";
        }
        else
        {
            var escaped = EscapeLike(trimmed);
            command.Parameters.AddWithValue("@search", escaped);
            command.CommandText =
                $"SELECT DISTINCT {quotedColumn} FROM {qualified} WHERE {quotedColumn} IS NOT NULL " +
                $"AND CAST({quotedColumn} AS TEXT) LIKE @search || '%' ESCAPE '\\' ORDER BY " +
                $"CASE WHEN CAST({quotedColumn} AS TEXT) = @search THEN 0 ELSE 1 END, {quotedColumn} LIMIT @limit;";
        }
        command.Parameters.AddWithValue("@limit", capped);
        var values = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.IsDBNull(0) ? null : SqliteValues.Materialize(reader.GetValue(0)));
        }
        return values;
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
