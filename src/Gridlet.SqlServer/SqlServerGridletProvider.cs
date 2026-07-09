using Gridlet.Abstractions;

namespace Gridlet.SqlServer;

/// <summary>The SQL Server implementation of the Gridlet provider boundary.</summary>
public sealed class SqlServerGridletProvider : IGridletProvider
{
    public string ProviderName => GridletProviderNames.SqlServer;

    public ISchemaReader Schema { get; } = new SqlServerSchemaReader();

    public ITableDataService Data { get; } = new SqlServerTableDataService();

    public IQueryRunner Query { get; } = new SqlServerQueryRunner();
}
