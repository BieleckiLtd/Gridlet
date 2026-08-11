using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>The SQLite implementation of the Gridlet provider boundary.</summary>
public sealed class SqliteGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider, IForeignKeyLookupProvider,
    ITableImportProvider
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

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
