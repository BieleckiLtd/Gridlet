using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>The SQL Server implementation of the Gridlet provider boundary.</summary>
public sealed class SqlServerGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider, IForeignKeyLookupProvider,
    ITableImportProvider, ISequenceProvider, IDatabaseSecurityProvider, ITriggerManagementProvider,
    IColumnDistinctValuesProvider
{
    public GridletProviderNames ProviderName => GridletProviderNames.SqlServer;

    public GridletProviderCapabilities Capabilities { get; } = new(
        DefaultSchema: "dbo",
        SupportsSchemas: true,
        SupportsViews: true,
        SupportsStoredProcedures: true,
        SupportsFunctions: true,
        SupportsTriggers: true,
        SupportsClusteredPrimaryKeys: true,
        SuggestedDataTypes:
        [
            "int", "bigint", "smallint", "tinyint", "bit", "nvarchar(50)", "nvarchar(100)",
            "nvarchar(max)", "varchar(50)", "decimal(18,2)", "money", "float", "date", "time",
            "datetime2", "datetimeoffset", "uniqueidentifier", "varbinary(max)",
        ],
        SelectExample: "SELECT TOP (100) * FROM {object};",
        CreateTriggerExample:
            "CREATE TRIGGER dbo.NewTrigger\nON dbo.SomeTable\nAFTER INSERT, UPDATE, DELETE\nAS\nBEGIN\n    SET NOCOUNT ON;\nEND;",
        ObjectEditMode: "Alter",
        SupportsCheckConstraints: true,
        SupportsUniqueConstraints: true,
        SupportsIndexes: true,
        SupportsSessions: true,
        SupportsQueryPlans: true,
        SupportsSequences: true,
        SupportsImport: true,
        SupportsDefaultConstraints: true,
        SupportsSecurityOverview: true,
        SupportsTriggerManagement: true);

    public ISchemaReader Schema { get; } = new SqlServerSchemaReader();

    public ITableDataService Data { get; } = new SqlServerTableDataService();

    public IQueryRunner Query { get; } = new SqlServerQueryRunner();

    public ITableWriteService Writes { get; } = new SqlServerTableWriteService();

    public ITableDdlService Ddl { get; } = new SqlServerTableDdlService();

    public Task<TableImportResult> ImportAsync(
        GridletConnectionContext context, string schema, string table, TableImport import,
        CancellationToken cancellationToken = default)
        => SqlServerTableImportService.ImportAsync(context, schema, table, import, cancellationToken);

    public Task CreateSequenceAsync(
        GridletConnectionContext context, SequenceDesign design,
        CancellationToken cancellationToken = default)
        => SqlServerSequenceService.CreateAsync(context, design, cancellationToken);

    public Task RestartSequenceAsync(
        GridletConnectionContext context, string schema, string name, string value,
        CancellationToken cancellationToken = default)
        => SqlServerSequenceService.RestartAsync(context, schema, name, value, cancellationToken);

    public Task<DatabaseSecurityOverview> GetSecurityOverviewAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
        => SqlServerSecurityService.GetOverviewAsync(context, cancellationToken);

    public Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
        => SqlServerTriggerService.GetAsync(context, cancellationToken);

    public Task SetTriggerEnabledAsync(
        GridletConnectionContext context,
        TriggerStateDesign trigger,
        CancellationToken cancellationToken = default)
        => SqlServerTriggerService.SetEnabledAsync(context, trigger, cancellationToken);

    public async Task<GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        return new GridletDatabaseSystemInfo("Microsoft SQL Server", connection.ServerVersion);
    }

    public async Task<IReadOnlyList<ForeignKeyLookupItem>> LookupForeignKeyAsync(
        GridletConnectionContext context, string schema, string table, string keyColumn,
        string labelColumn, IReadOnlyList<object?> keys, string? search, int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        var key = SqlServerIdentifier.Quote(keyColumn);
        var label = SqlServerIdentifier.Quote(labelColumn);
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
            var searchPredicates = new List<string> { $"CONVERT(nvarchar(max), {key}) = @search" };
            if (trimmed.Length >= 2)
            {
                command.Parameters.AddWithValue("@contains", $"%{EscapeLike(trimmed)}%");
                searchPredicates.Add($"CONVERT(nvarchar(max), {label}) LIKE @contains");
            }
            predicates.Add($"({string.Join(" OR ", searchPredicates)})");
            order = $"CASE WHEN CONVERT(nvarchar(max), {key}) = @search THEN 0 " +
                $"WHEN CONVERT(nvarchar(max), {label}) = @search THEN 1 " +
                $"WHEN CONVERT(nvarchar(max), {label}) LIKE @search + '%' THEN 2 ELSE 3 END, " +
                $"CONVERT(nvarchar(max), {label}), CONVERT(nvarchar(max), {key})";
        }

        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 50));
        command.CommandText = $"SELECT TOP (@limit) {key}, {label} FROM " +
            $"{SqlServerIdentifier.QuoteQualified(schema, table)}" +
            (predicates.Count > 0 ? $" WHERE {string.Join(" OR ", predicates)}" : "") +
            $" ORDER BY {order};";
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
        var quotedColumn = SqlServerIdentifier.Quote(column);
        var qualified = SqlServerIdentifier.QuoteQualified(schema, table);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);

        string? columnType = null;
        // Validate the table and column exist before reaching dynamic SQL and capture the type for
        // distribution heuristics (numeric/date range predicates want a spread, not just the low end).
        await using (var validate = connection.CreateCommand())
        {
            validate.CommandText = """
                SELECT t.name
                FROM sys.columns c
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID(@name) AND c.name = @col;
                """;
            validate.Parameters.AddWithValue("@name", qualified);
            validate.Parameters.AddWithValue("@col", column);
            var typeObj = await validate.ExecuteScalarAsync(cancellationToken);
            if (typeObj is null) throw new GridletValidationException($"Column '{column}' does not exist on {qualified}.");
            columnType = Convert.ToString(typeObj);
        }

        var capped = Math.Clamp(limit, 1, 50);
        var trimmed = search?.Trim();
        // When the request is a range distribution (empty prefix, small limit) on a numeric/date
        // column, return 10 values spread across the full distinct set instead of the 10 smallest.
        // For SQLite TEXT columns that store ISO8601 dates, the declared type is TEXT, so also
        // consider the column name (e.g. OrderedAtUtc) as a date hint.
        var isDateByName = column.Contains("date", StringComparison.OrdinalIgnoreCase)
            || column.Contains("time", StringComparison.OrdinalIgnoreCase)
            || column.Contains("atutc", StringComparison.OrdinalIgnoreCase);
        var isDistribution = string.IsNullOrEmpty(trimmed) && capped <= 10
            && columnType is not null
            && (columnType.StartsWith("int", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("bigint", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("smallint", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("tinyint", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("decimal", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("numeric", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("float", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("real", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("money", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("date", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("time", StringComparison.OrdinalIgnoreCase)
                || columnType.StartsWith("datetime", StringComparison.OrdinalIgnoreCase)
                || isDateByName);
        if (isDistribution)
        {
            // Count distinct to size the sample.
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
                while (await r.ReadAsync(cancellationToken)) allValues.Add(r.IsDBNull(0) ? null : SqlServerValues.Materialize(r.GetValue(0)));
                return allValues;
            }
            // Sample evenly: OFFSET step = count / limit.
            var step = Math.Max(1, distinctCount / capped);
            var sampled = new List<object?>();
            for (var i = 0; i < capped; i++)
            {
                var offset = i * step;
                // OFFSET/FETCH requires ORDER BY, supported on SQL Server 2012+.
                await using var offCmd = connection.CreateCommand();
                offCmd.CommandText = BuildDistributionSampleSql(quotedColumn, qualified);
                offCmd.Parameters.AddWithValue("@off", offset);
                var val = await offCmd.ExecuteScalarAsync(cancellationToken);
                if (val is not null && val is not DBNull) sampled.Add(SqlServerValues.Materialize(val));
            }
            // Deduplicate while preserving order (distinct offsets can collide on small sets).
            return sampled.Distinct().ToArray();
        }

        await using var command = connection.CreateCommand();
        // DISTINCT on a column with many distinct values can be expensive; TOP limits the work.
        // Prefix filtering reduces the set further when the user has typed something.
        if (string.IsNullOrEmpty(trimmed))
        {
            command.CommandText = $"SELECT DISTINCT TOP (@limit) {quotedColumn} FROM {qualified} WHERE {quotedColumn} IS NOT NULL ORDER BY {quotedColumn};";
        }
        else
        {
            var (exact, pattern) = BuildDistinctValueSearch(trimmed);
            command.Parameters.AddWithValue("@search", exact);
            command.Parameters.AddWithValue("@pattern", pattern);
            command.CommandText =
                $"SELECT DISTINCT TOP (@limit) {quotedColumn} FROM {qualified} WHERE {quotedColumn} IS NOT NULL " +
                $"AND CONVERT(nvarchar(max), {quotedColumn}) LIKE @pattern ORDER BY " +
                $"CASE WHEN CONVERT(nvarchar(max), {quotedColumn}) = @search THEN 0 ELSE 1 END, {quotedColumn};";
        }

        command.Parameters.AddWithValue("@limit", capped);
        var values = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.IsDBNull(0) ? null : SqlServerValues.Materialize(reader.GetValue(0)));
        }
        return values;
    }

    private static string EscapeLike(string value)
        => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    internal static (string Exact, string Pattern) BuildDistinctValueSearch(string value)
        => (value, $"{EscapeLike(value)}%");

    internal static string BuildDistributionSampleSql(string quotedColumn, string qualified)
        => $"SELECT v FROM (SELECT DISTINCT {quotedColumn} AS v FROM {qualified} WHERE {quotedColumn} IS NOT NULL) t " +
           "ORDER BY v OFFSET @off ROWS FETCH NEXT 1 ROWS ONLY;";
}
