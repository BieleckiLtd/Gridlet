using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>The SQL Server implementation of the Gridlet provider boundary.</summary>
public sealed class SqlServerGridletProvider :
    IGridletProvider, IGridletProviderMetadata, IGridletDatabaseSystemInfoProvider
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
        SupportsIndexes: true);

    public ISchemaReader Schema { get; } = new SqlServerSchemaReader();

    public ITableDataService Data { get; } = new SqlServerTableDataService();

    public IQueryRunner Query { get; } = new SqlServerQueryRunner();

    public ITableWriteService Writes { get; } = new SqlServerTableWriteService();

    public ITableDdlService Ddl { get; } = new SqlServerTableDdlService();

    public async Task<GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        return new GridletDatabaseSystemInfo("Microsoft SQL Server", connection.ServerVersion);
    }
}
