using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>The SQL Server implementation of the Gridlet provider boundary.</summary>
public sealed class SqlServerGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider, IForeignKeyLookupProvider,
    ITableImportProvider, ISequenceProvider
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
        SupportsImport: true);

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

    private static string EscapeLike(string value)
        => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
