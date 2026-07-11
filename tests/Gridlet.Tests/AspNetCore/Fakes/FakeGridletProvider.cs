using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Tests.AspNetCore.Fakes;

/// <summary>An in-memory provider so endpoint behaviour can be tested without a database.</summary>
public sealed class FakeGridletProvider :
    IGridletProvider, ISchemaReader, ITableDataService, IQueryRunner, ITableWriteService, ITableDdlService
{
    public const string Name = "Fake";

    /// <summary>Human-readable record of every write/DDL call, for assertions.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Parameters passed to the most recent query execution.</summary>
    public IReadOnlyDictionary<string, object?>? LastQueryParameters { get; private set; }

    public QueryRequestOptions? LastQueryOptions { get; private set; }

    public string ProviderName => Name;

    public ISchemaReader Schema => this;

    public ITableDataService Data => this;

    public IQueryRunner Query => this;

    public ITableWriteService Writes => this;

    public ITableDdlService Ddl => this;

    // ---- schema ----

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

    public Task<IReadOnlyList<SchemaInfo>> GetSchemasAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SchemaInfo>>(
        [
            new SchemaInfo("dbo", "dbo"),
            new SchemaInfo("empty_schema", "app_user"),
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

    // ---- data ----

    public Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context, string schema, string name, TableDataRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TableDataPage(
            [new ResultColumn("Id", "int")],
            [[1], [2]],
            request.Page,
            request.PageSize,
            TotalRows: 2));

    // ---- queries ----

    public Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context, string sql, QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        LastQueryParameters = parameters;
        LastQueryOptions = options;
        return sql == "boom"
            ? throw new GridletQueryException("kaboom")
            : Task.FromResult(new QueryResult(
                [new QueryResultSet([new ResultColumn("Answer", "int")], [[42]], Truncated: false)],
                RecordsAffected: -1,
                Messages: ["hello from fake"],
                DurationMs: 1));
    }

    // ---- writes ----

    public Task<int> InsertRowAsync(
        GridletConnectionContext context, string schema, string table,
        IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        Calls.Add($"insert {schema}.{table} ({string.Join(",", values.Keys)})");
        return Task.FromResult(1);
    }

    public Task<int> UpdateRowAsync(
        GridletConnectionContext context, string schema, string table,
        IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"update {schema}.{table} key({string.Join(",", key.Keys)}) set({string.Join(",", values.Keys)})");
        return Task.FromResult(1);
    }

    public Task<int> DeleteRowAsync(
        GridletConnectionContext context, string schema, string table,
        IReadOnlyDictionary<string, object?> key, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete {schema}.{table} key({string.Join(",", key.Keys)})");
        return Task.FromResult(1);
    }

    // ---- ddl ----

    public Task CreateSchemaAsync(
        GridletConnectionContext context, SchemaDesign design, CancellationToken cancellationToken = default)
    {
        Calls.Add($"createSchema {design.Name} owner={design.Owner}");
        return Task.CompletedTask;
    }

    public Task AlterSchemaOwnerAsync(
        GridletConnectionContext context, string schema, string owner, CancellationToken cancellationToken = default)
    {
        Calls.Add($"alterSchemaOwner {schema} owner={owner}");
        return Task.CompletedTask;
    }

    public Task DropSchemaAsync(
        GridletConnectionContext context, string schema, CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropSchema {schema}");
        return Task.CompletedTask;
    }

    public Task CreateTableAsync(
        GridletConnectionContext context, TableDesign design, CancellationToken cancellationToken = default)
    {
        Calls.Add($"createTable {design.Schema}.{design.Name} ({design.Columns.Count} columns)");
        return Task.CompletedTask;
    }

    public Task AddColumnAsync(
        GridletConnectionContext context, string schema, string table, ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addColumn {schema}.{table}.{column.Name}");
        return Task.CompletedTask;
    }

    public Task AlterColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName, ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"alterColumn {schema}.{table}.{columnName} -> {column.Name}");
        return Task.CompletedTask;
    }

    public Task DropColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropColumn {schema}.{table}.{columnName}");
        return Task.CompletedTask;
    }

    public Task DropTableAsync(
        GridletConnectionContext context, string schema, string table, CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropTable {schema}.{table}");
        return Task.CompletedTask;
    }

    public Task DropObjectAsync(
        GridletConnectionContext context, string schema, string name, DbObjectType type,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropObject {type} {schema}.{name}");
        return Task.CompletedTask;
    }
}
