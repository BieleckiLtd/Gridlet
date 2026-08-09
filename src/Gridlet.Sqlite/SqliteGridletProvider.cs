using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>The SQLite implementation of the Gridlet provider boundary.</summary>
public sealed class SqliteGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider
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
        SupportsSessions: true);

    public ISchemaReader Schema { get; } = new SqliteSchemaReader();

    public ITableDataService Data { get; } = new SqliteTableDataService();

    public IQueryRunner Query { get; } = new SqliteQueryRunner();

    public ITableWriteService Writes { get; } = new SqliteTableWriteService();

    public ITableDdlService Ddl { get; } = new SqliteTableDdlService();

    public async Task<GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return new GridletDatabaseSystemInfo("SQLite", connection.ServerVersion);
    }
}
