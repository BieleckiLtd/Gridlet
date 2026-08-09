using System.Data.Common;
using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Tests.AspNetCore.Fakes;

/// <summary>An in-memory provider so endpoint behaviour can be tested without a database.</summary>
public sealed class FakeGridletProvider :
    IGridletProvider, IGridletProviderMetadata, ISchemaReader, ITableDataService, IQueryRunner,
    IQuerySessionRunner, IQueryPlanRunner, ITableWriteService, ITableDdlService
{
    public const GridletProviderNames Name = GridletProviderNames.SqlServer;

    /// <summary>Human-readable record of every write/DDL call, for assertions.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Parameters passed to the most recent query execution.</summary>
    public IReadOnlyDictionary<string, object?>? LastQueryParameters { get; private set; }

    public QueryRequestOptions? LastQueryOptions { get; private set; }

    public string? LastQuerySql { get; private set; }

    public GridletProviderNames ProviderName => Name;

    public GridletProviderCapabilities Capabilities { get; } = new(
        DefaultSchema: "dbo",
        SupportsSchemas: true,
        SupportsViews: true,
        SupportsStoredProcedures: true,
        SupportsFunctions: true,
        SupportsTriggers: true,
        SupportsClusteredPrimaryKeys: true,
        SuggestedDataTypes: ["int", "nvarchar(100)"],
        SelectExample: "SELECT TOP (100) * FROM {object};",
        CreateTriggerExample:
            "CREATE TRIGGER dbo.NewTrigger\nON dbo.Customers\nAFTER INSERT\nAS\nBEGIN\n    SELECT 1;\nEND;",
        ObjectEditMode: "Alter",
        SupportsCheckConstraints: true,
        SupportsUniqueConstraints: true,
        SupportsIndexes: true,
        SupportsSessions: true,
        SupportsQueryPlans: true);

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
            new DbObjectInfo("dbo", "NoKeys", DbObjectType.Table),
            new DbObjectInfo("dbo", "Heap", DbObjectType.Table),
            new DbObjectInfo("dbo", "SearchIndex", DbObjectType.Table, "VirtualTable"),
            new DbObjectInfo("dbo", "Customers_fts_data", DbObjectType.Table, "Shadow", IsInternal: true),
            new DbObjectInfo("dbo", "vw_Orders", DbObjectType.View),
            new DbObjectInfo("dbo", "RefreshOrders", DbObjectType.StoredProcedure),
            new DbObjectInfo("dbo", "OrderCount", DbObjectType.ScalarFunction),
            new DbObjectInfo("dbo", "AuditCustomers", DbObjectType.Trigger),
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
        => name switch
        {
            "Missing" => Task.FromException<TableDefinition>(
                new GridletObjectNotFoundException($"{schema}.{name}")),

            // A heap: no primary key, and the value that identifies a row is not one of its columns.
            "Heap" => Task.FromResult(new TableDefinition(
                new DbObjectInfo(schema, name, DbObjectType.Table),
                [new ColumnInfo("Name", "nvarchar(100)", false, false, false, false, null, 0)],
                [],
                [],
                [],
                [],
                new RowIdentityInfo(RowIdentityKinds.RowId, ["rowid"]))),

            _ => Task.FromResult(new TableDefinition(
            new DbObjectInfo(schema, name, DbObjectType.Table),
            [
                new ColumnInfo("Id", "int", false, true, false, name != "NoKeys", null, 0),
                new ColumnInfo("Name", "nvarchar(100)", false, false, false, false, null, 1),
                new ColumnInfo("SysStart", "datetime2", false, false, true, false, null, 2,
                    "GENERATED ALWAYS", IsHidden: true),
            ],
            name == "NoKeys"
                ? []
                : [
                    new IndexInfo("PK_" + name, "CLUSTERED", true, true, ["Id"]),
                    new IndexInfo("IX_" + name + "_Name", "NONCLUSTERED", false, false, ["Name"],
                        [new IndexKeyInfo("Name", 1, IsDescending: true, Collation: "Latin1_General_CI_AS")],
                        ["Id"], "[Name] IS NOT NULL", IsClustered: false, IsColumnstore: false,
                        FillFactor: 80, IsDisabled: true),
                ],
            [],
            [new CheckConstraintInfo("CK_" + name + "_Name", "length([Name]) > 0", IsDisabled: true,
                IsTrusted: false),
             new CheckConstraintInfo(null, "[Id] > 0", Ordinal: 0)],
            [new UniqueConstraintInfo("UQ_" + name + "_Name", [
                new IndexKeyInfo("Name", 1, IsDescending: true, Collation: "NOCASE")],
                IsClustered: true, FillFactor: 90, IsDisabled: true)],
            name == "NoKeys"
                ? null
                : new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]))),
        };

    public Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context, string schema, string name, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(name == "AuditCustomers"
            ? $"CREATE TRIGGER {schema}.{name} ON {schema}.Customers AFTER INSERT AS SELECT 1;"
            : $"CREATE VIEW {schema}.{name} AS SELECT 1 AS One;");

    // ---- routines ----

    public Task<RoutineDefinition> GetRoutineDefinitionAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
        => name switch
        {
            "RefreshOrders" => Task.FromResult(new RoutineDefinition(
                new DbObjectInfo(schema, name, DbObjectType.StoredProcedure),
                [
                    new RoutineParameterInfo("@ReturnValue", "int", 0, IsOutput: true, IsReturnValue: true),
                    new RoutineParameterInfo("@Since", "datetime2(7)", 1),
                    new RoutineParameterInfo("@RowsChanged", "int", 2, IsOutput: true),
                ])),
            "OrderCount" => Task.FromResult(new RoutineDefinition(
                new DbObjectInfo(schema, name, DbObjectType.ScalarFunction),
                [
                    new RoutineParameterInfo("@ReturnValue", "int", 0, IsReturnValue: true),
                    new RoutineParameterInfo("@CustomerId", "int", 1),
                ])),
            _ => Task.FromException<RoutineDefinition>(
                new GridletValidationException($"{schema}.{name} is not a stored procedure or function.")),
        };

    /// <summary>A stand-in script: enough to prove the arguments reached the provider.</summary>
    public string BuildRoutineExecuteScript(
        RoutineDefinition routine, IReadOnlyDictionary<string, RoutineArgument> arguments)
    {
        var rendered = arguments
            .OrderBy(argument => argument.Key, StringComparer.OrdinalIgnoreCase)
            .Select(argument => $"{argument.Key} = {(argument.Value.IsNull ? "NULL" : argument.Value.Value)}");
        Calls.Add($"script {routine.Object.Schema}.{routine.Object.Name} ({string.Join(", ", rendered)})");
        return $"EXEC {routine.Object.Schema}.{routine.Object.Name} {string.Join(", ", rendered)};";
    }

    // ---- data ----

    /// <summary>The filters the most recent page request carried, so their parsing can be asserted.</summary>
    public IReadOnlyList<TableDataFilter>? LastDataFilters { get; private set; }

    public Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context, string schema, string name, TableDataRequest request,
        CancellationToken cancellationToken = default)
    {
        LastDataFilters = request.Filters;
        return GetPageCore(name, request);
    }

    private static Task<TableDataPage> GetPageCore(string name, TableDataRequest request)
        => Task.FromResult(name == "Heap"
            ? new TableDataPage(
                [new ResultColumn("Name", "nvarchar(100)")],
                [["Ada"], ["Grace"]],
                request.Page,
                request.PageSize,
                TotalRows: 2,
                RowIdentity: new RowIdentityInfo(RowIdentityKinds.RowId, ["rowid"]),
                RowKeys: [[101], [102]])
            : new TableDataPage(
                [new ResultColumn("Id", "int"), new ResultColumn("Name", "nvarchar(100)")],
                [[1, "Ada"], [2, "Grace"]],
                request.Page,
                request.PageSize,
                TotalRows: 2,
                RowIdentity: name == "NoKeys" ? null : new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]),
                RowKeys: name == "NoKeys" ? null : [[1], [2]]));

    // ---- execution plans ----

    public Task<QueryPlan> GetPlanAsync(
        GridletConnectionContext context, string sql, QueryPlanMode mode,
        QueryRequestOptions options, CancellationToken cancellationToken = default)
    {
        Calls.Add($"plan.{mode.ToString().ToLowerInvariant()} {sql}");
        if (sql == "boom")
        {
            throw new GridletQueryException("kaboom");
        }

        return Task.FromResult(new QueryPlan(
            mode,
            "showplan-xml",
            [
                new QueryPlanNode("SELECT", sql, EstimatedRows: 120, EstimatedCost: 0.04, Children:
                [
                    new QueryPlanNode("Clustered Index Scan", "Customers.PK_Customers",
                        EstimatedRows: 120,
                        ActualRows: mode == QueryPlanMode.Actual ? 118 : null,
                        EstimatedCost: 0.04,
                        Warnings: ["Missing index on Customers (Name)"]),
                ]),
            ],
            "<ShowPlanXML />",
            mode == QueryPlanMode.Actual ? ["Table 'Customers'. Scan count 1, logical reads 3."] : []));
    }

    // ---- pinned sessions ----
    //
    // The connection is a stand-in that only tracks open/closed, which is all Gridlet's session
    // handling asks of it. The transaction depth is counted here, so a session's state behaves the
    // way a database's would without needing one.

    /// <summary>Transaction depth per session connection, so state survives across calls.</summary>
    private readonly Dictionary<DbConnection, int> transactionDepths = [];

    public Task<DbConnection> OpenSessionAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
    {
        Calls.Add($"session.open {context.ConnectionName}/{context.Database}");
        DbConnection connection = new FakeSessionConnection();
        connection.Open();
        transactionDepths[connection] = 0;
        return Task.FromResult(connection);
    }

    public IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        DbConnection connection, string sql, QueryRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"session.query {sql}");
        return StreamAsync(
            new GridletConnectionContext(new GridletConnectionOptions { Name = "Session" }, null),
            sql, options, parameters: null, cancellationToken);
    }

    public Task<TransactionStatus> GetTransactionStatusAsync(
        DbConnection connection, CancellationToken cancellationToken = default)
        => Task.FromResult(Status(transactionDepths.GetValueOrDefault(connection)));

    public Task<TransactionStatus> RunTransactionCommandAsync(
        DbConnection connection, TransactionCommand command, CancellationToken cancellationToken = default)
    {
        Calls.Add($"session.{command.ToString().ToLowerInvariant()}");
        var depth = transactionDepths.GetValueOrDefault(connection);
        if (command != TransactionCommand.Begin && depth == 0)
        {
            throw new GridletQueryException("There is no transaction to end.");
        }

        depth = command == TransactionCommand.Begin ? depth + 1 : depth - 1;
        transactionDepths[connection] = depth;
        return Task.FromResult(Status(depth));
    }

    private static TransactionStatus Status(int depth)
        => depth > 0 ? new TransactionStatus(true, depth) : TransactionStatus.None;

    /// <summary>A connection that is only ever asked whether it is open, and then closed.</summary>
    private sealed class FakeSessionConnection : DbConnection
    {
        private System.Data.ConnectionState state = System.Data.ConnectionState.Closed;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "fake";

        public override string Database => "FakeDb";

        public override string DataSource => "fake";

        public override string ServerVersion => "1.0";

        public override System.Data.ConnectionState State => state;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => state = System.Data.ConnectionState.Closed;

        public override void Open() => state = System.Data.ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    // ---- queries ----

    public Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context, string sql, QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        LastQuerySql = sql;
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

    /// <summary>
    /// Streams a single-row result set. Recognised sentinels: <c>boom</c> fails before any event is
    /// emitted (clean status code), and <c>stream-boom</c> fails after a row has streamed (in-body
    /// error marker). Records the query options so cap behaviour can be asserted.
    /// </summary>
    public async IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        GridletConnectionContext context, string sql, QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastQuerySql = sql;
        LastQueryParameters = parameters;
        LastQueryOptions = options;

        if (sql == "boom")
        {
            throw new GridletQueryException("kaboom");
        }
        if (sql == "unexpected-boom")
        {
            throw new InvalidOperationException("SECRET_PUBLISHED_SENTINEL");
        }

        await Task.Yield();

        if (sql == "no-results")
        {
            yield return new QueryStreamEvent("started");
            yield return new QueryStreamEvent("completed", RecordsAffected: 0, DurationMs: 1);
            yield break;
        }

        // "many:N" streams N rows in batches, to prove uncapped streaming past the global default.
        if (sql.StartsWith("many:", StringComparison.Ordinal) &&
            int.TryParse(sql["many:".Length..], out var total))
        {
            yield return new QueryStreamEvent("started");
            yield return new QueryStreamEvent("resultSet", 0, [new ResultColumn("N", "int")]);
            var batch = new List<object?[]>();
            for (var i = 0; i < total; i++)
            {
                batch.Add([i]);
                if (batch.Count == 500)
                {
                    yield return new QueryStreamEvent("rows", 0, Rows: batch.ToArray());
                    batch = [];
                }
            }

            if (batch.Count > 0)
            {
                yield return new QueryStreamEvent("rows", 0, Rows: batch.ToArray());
            }

            yield return new QueryStreamEvent("completed", RecordsAffected: -1, DurationMs: 1);
            yield break;
        }

        yield return new QueryStreamEvent("started");
        yield return new QueryStreamEvent("resultSet", 0, [new ResultColumn("Answer", "int")]);
        yield return new QueryStreamEvent("rows", 0, Rows: [[42]]);

        if (sql == "stream-boom")
        {
            throw new GridletQueryException("mid-stream kaboom");
        }
        if (sql == "stream-unexpected-boom")
        {
            throw new InvalidOperationException("SECRET_PUBLISHED_SENTINEL");
        }

        yield return new QueryStreamEvent("resultSetCompleted", 0, Truncated: false);
        yield return new QueryStreamEvent("message", Message: "hello from fake");
        yield return new QueryStreamEvent("completed", RecordsAffected: -1, DurationMs: 1);
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

    public Task AddPrimaryKeyAsync(
        GridletConnectionContext context, string schema, string table, PrimaryKeyDesign primaryKey,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addPrimaryKey {schema}.{table}.{primaryKey.Name}");
        return Task.CompletedTask;
    }

    public Task AddCheckConstraintAsync(
        GridletConnectionContext context, string schema, string table, CheckConstraintDesign checkConstraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addCheckConstraint {schema}.{table}.{checkConstraint.Name ?? "(unnamed)"} expression={checkConstraint.Expression}");
        return Task.CompletedTask;
    }

    public Task DropCheckConstraintAsync(
        GridletConnectionContext context, string schema, string table, ConstraintReference constraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropCheckConstraint {schema}.{table}.{constraint.Name ?? $"#{constraint.Ordinal}"}");
        return Task.CompletedTask;
    }

    public Task AddUniqueConstraintAsync(
        GridletConnectionContext context, string schema, string table, UniqueConstraintDesign uniqueConstraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addUniqueConstraint {schema}.{table}.{uniqueConstraint.Name ?? "(unnamed)"} ({string.Join(",", uniqueConstraint.Columns.Select(c => c.Column))})");
        return Task.CompletedTask;
    }

    public Task DropUniqueConstraintAsync(
        GridletConnectionContext context, string schema, string table, ConstraintReference constraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropUniqueConstraint {schema}.{table}.{constraint.Name ?? $"#{constraint.Ordinal}"}");
        return Task.CompletedTask;
    }

    public Task CreateIndexAsync(
        GridletConnectionContext context, string schema, string table, IndexDesign index,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"createIndex {schema}.{table}.{index.Name} ({string.Join(",", index.KeyColumns.Select(c => $"{c.Column}:{(c.IsDescending ? "DESC" : "ASC")}"))}) unique={index.IsUnique} filter={index.FilterExpression}");
        return Task.CompletedTask;
    }

    public Task DropIndexAsync(
        GridletConnectionContext context, string schema, string table, string indexName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropIndex {schema}.{table}.{indexName}");
        return Task.CompletedTask;
    }

    public Task AddForeignKeyAsync(
        GridletConnectionContext context, string schema, string table, ForeignKeyDesign foreignKey,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addForeignKey {schema}.{table}.{foreignKey.Name}");
        return Task.CompletedTask;
    }

    public Task DropConstraintAsync(
        GridletConnectionContext context, string schema, string table, string constraintName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropConstraint {schema}.{table}.{constraintName}");
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
