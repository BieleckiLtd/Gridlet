using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Tests.AspNetCore.Fakes;

/// <summary>An in-memory provider so endpoint behaviour can be tested without a database.</summary>
public sealed class FakeGridletProvider : IGridletProvider, ISchemaReader, ITableDataService, IQueryRunner
{
    public const string Name = "Fake";

    public string ProviderName => Name;

    public ISchemaReader Schema => this;

    public ITableDataService Data => this;

    public IQueryRunner Query => this;

    public Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DatabaseInfo>>(
        [
            new DatabaseInfo("FakeDb", IsSystem: false),
            new DatabaseInfo("master", IsSystem: true),
        ]);

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DbObjectInfo>>(
        [
            new DbObjectInfo("dbo", "Customers", DbObjectType.Table),
            new DbObjectInfo("dbo", "vw_Orders", DbObjectType.View),
        ]);

    public Task<TableDefinition> GetTableDefinitionAsync(
        GridletConnectionContext context, string schema, string name, CancellationToken cancellationToken = default)
        => Task.FromResult(new TableDefinition(
            new DbObjectInfo(schema, name, DbObjectType.Table),
            [new ColumnInfo("Id", "int", false, true, false, true, null, 0)],
            [new IndexInfo("PK_" + name, "CLUSTERED", true, true, ["Id"])],
            []));

    public Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context, string schema, string name, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>($"CREATE VIEW {schema}.{name} AS SELECT 1 AS One;");

    public Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context, string schema, string name, TableDataRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TableDataPage(
            [new ResultColumn("Id", "int")],
            [[1], [2]],
            request.Page,
            request.PageSize,
            TotalRows: 2));

    public Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context, string sql, QueryRequestOptions options,
        CancellationToken cancellationToken = default)
        => sql == "boom"
            ? throw new GridletQueryException("kaboom")
            : Task.FromResult(new QueryResult(
                [new QueryResultSet([new ResultColumn("Answer", "int")], [[42]], Truncated: false)],
                RecordsAffected: -1,
                Messages: ["hello from fake"],
                DurationMs: 1));
}
