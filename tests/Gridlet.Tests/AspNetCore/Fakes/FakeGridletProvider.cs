using System.Data.Common;
using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Tests.AspNetCore.Fakes;

/// <summary>An in-memory provider so endpoint behaviour can be tested without a database.</summary>
public sealed class FakeGridletProvider :
    IGridletProvider, IGridletProviderMetadata, ISchemaReader, ITableDataService, IQueryRunner,
    IQuerySessionRunner, IQueryPlanRunner, ITableWriteService, ITableDdlService,
    IForeignKeyLookupProvider, ITableImportProvider, ISequenceProvider,
    IDatabaseSecurityProvider, ITriggerManagementProvider, IColumnDistinctValuesProvider
{
    public const GridletProviderNames Name = GridletProviderNames.SqlServer;

    /// <summary>Human-readable record of every write/DDL call, for assertions.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Parameters passed to the most recent query execution.</summary>
    public IReadOnlyDictionary<string, object?>? LastQueryParameters { get; private set; }

    public QueryRequestOptions? LastQueryOptions { get; private set; }

    public string? LastQuerySql { get; private set; }

    public IReadOnlyDictionary<string, object?>? LastWriteValues { get; private set; }

    public TableImport? LastImport { get; private set; }

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
        SupportsQueryPlans: true,
        SupportsSequences: true,
        SupportsImport: true,
        SupportsDefaultConstraints: true,
        SupportsSecurityOverview: true,
        SupportsTriggerManagement: true);

    public ISchemaReader Schema => this;

    public ITableDataService Data => this;

    public IQueryRunner Query => this;

    public ITableWriteService Writes => this;

    public ITableDdlService Ddl => this;

    public Task<DatabaseSecurityOverview> GetSecurityOverviewAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(new DatabaseSecurityOverview(
            "app_user", "app_login", "app_login",
            [
                new DatabasePrincipalInfo("app_user", "SQL_USER", "INSTANCE", "dbo"),
                new DatabasePrincipalInfo("report_reader", "DATABASE_ROLE"),
            ],
            [new DatabaseRoleMembershipInfo("report_reader", "app_user")],
            [new DatabasePermissionInfo("report_reader", "dbo", "GRANT", "SELECT", "SCHEMA", "[dbo]")],
            [new EffectivePermissionInfo("DATABASE", "CONNECT"), new EffectivePermissionInfo("DATABASE", "SELECT")]));

    public Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(
        GridletConnectionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TriggerInfo>>(
        [
            new TriggerInfo("AuditCustomers", TriggerScopes.Object, false, ["INSERT"],
                "CREATE TRIGGER dbo.AuditCustomers ON dbo.Customers AFTER INSERT AS SELECT 1;",
                "dbo", "dbo", "Customers"),
            new TriggerInfo("AuditDatabaseDdl", TriggerScopes.Database, true, ["CREATE_TABLE"],
                "CREATE TRIGGER AuditDatabaseDdl ON DATABASE FOR CREATE_TABLE AS SELECT 1;"),
            new TriggerInfo("AuditLogins", TriggerScopes.Server, false, ["LOGON"],
                "CREATE TRIGGER AuditLogins ON ALL SERVER FOR LOGON AS SELECT 1;"),
        ]);

    public Task SetTriggerEnabledAsync(
        GridletConnectionContext context, TriggerStateDesign trigger,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"setTriggerState {trigger.Scope}.{trigger.Schema}.{trigger.Name} enabled={trigger.Enabled}");
        return Task.CompletedTask;
    }

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
            new DbObjectInfo("dbo", "Customers", DbObjectType.Table,
                Description: "People who buy from the store"),
            new DbObjectInfo("dbo", "Orders", DbObjectType.Table),
            new DbObjectInfo("dbo", "Pizzas", DbObjectType.Table),
            new DbObjectInfo("dbo", "NoKeys", DbObjectType.Table),
            // Two tables with more rows than one page, one addressable and one not, so paging can
            // be told apart from reading everything at once.
            new DbObjectInfo("dbo", "Ledger", DbObjectType.Table),
            new DbObjectInfo("dbo", "LedgerHeap", DbObjectType.Table),
            new DbObjectInfo("dbo", "Heap", DbObjectType.Table),
            new DbObjectInfo("dbo", "SearchIndex", DbObjectType.Table, "virtual"),
            new DbObjectInfo("dbo", "Customers_fts_data", DbObjectType.Table, "shadow", IsInternal: true),
            new DbObjectInfo("dbo", "vw_Orders", DbObjectType.View),
            new DbObjectInfo("dbo", "RefreshOrders", DbObjectType.StoredProcedure),
            new DbObjectInfo("dbo", "OrderCount", DbObjectType.ScalarFunction),
            new DbObjectInfo("dbo", "AuditCustomers", DbObjectType.Trigger),
            new DbObjectInfo("dbo", "OrderNumbers", DbObjectType.Sequence,
                Description: "Order number generator"),
            new DbObjectInfo("dbo", "AccountNumber", DbObjectType.UserDefinedType, "alias"),
            new DbObjectInfo("dbo", "OrderItems", DbObjectType.UserDefinedType, "table"),
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

            // A routine is not a table, and a strict provider says so rather than returning an
            // empty definition. Anything that only needs the object's identity must not ask.
            "RefreshOrders" or "OrderCount" => Task.FromException<TableDefinition>(
                new GridletValidationException($"{schema}.{name} is not a table or view.")),

            // A heap: no primary key, and the value that identifies a row is not one of its columns.
            "Heap" => Task.FromResult(new TableDefinition(
                new DbObjectInfo(schema, name, DbObjectType.Table),
                [new ColumnInfo("Name", "nvarchar(100)", false, false, false, false, null, 0)],
                [],
                [],
                [],
                [],
                new RowIdentityInfo(RowIdentityKinds.RowId, ["rowid"]))),

            "Orders" => Task.FromResult(new TableDefinition(
                new DbObjectInfo(schema, name, DbObjectType.Table),
                [
                    new ColumnInfo("Id", "int", false, true, false, true, null, 0),
                    new ColumnInfo("PizzaId", "int", false, false, false, false, null, 1),
                    new ColumnInfo("Promotion", "nvarchar(100)", true, false, false, false, null, 2),
                ],
                [new IndexInfo("PK_Orders", "CLUSTERED", true, true, ["Id"])],
                [new ForeignKeyInfo("FK_Orders_Pizzas", "dbo", "Pizzas",
                    [new ForeignKeyColumnPair("PizzaId", "Id")])],
                [], [], new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]))),

            _ => Task.FromResult(new TableDefinition(
            new DbObjectInfo(schema, name, DbObjectType.Table,
                Description: name == "Customers" ? "People who buy from the store" : null),
            [
                new ColumnInfo("Id", "int", false, true, false, name != "NoKeys", null, 0),
                new ColumnInfo("Name", "nvarchar(100)", false, false, false, false, "('N/A')", 1,
                    Description: name == "Customers" ? "Customer display name" : null),
                ..(name == "Customers"
                    ? new[] { new ColumnInfo("Status", "int", true, false, false, false, null, 2) }
                    : Array.Empty<ColumnInfo>()),
                new ColumnInfo("SysStart", "datetime2", false, false,
                    name is not ("Ledger" or "LedgerHeap"), false, null, name == "Customers" ? 3 : 2,
                    name is "Ledger" or "LedgerHeap" ? null : "GENERATED ALWAYS",
                    IsHidden: name is not ("Ledger" or "LedgerHeap")),
                ..(name is "Ledger" or "LedgerHeap"
                    ? new[] { new ColumnInfo("SysEnd", "datetime2", false, false, false, false, null, 3) }
                    : Array.Empty<ColumnInfo>()),
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
            name is "NoKeys" or "LedgerHeap"
                ? null
                : new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]),
            DefaultConstraints: [new DefaultConstraintInfo("DF_" + name + "_Name", "('N/A')", "Name")],
            Temporal: name switch
            {
                "Ledger" => new TemporalTableInfo(TemporalTableKinds.SystemVersioned,
                    "dbo", "LedgerHeap", "SysStart", "SysEnd", 6, "MONTH"),
                "LedgerHeap" => new TemporalTableInfo(TemporalTableKinds.HistoryTable,
                    "dbo", "Ledger", "SysStart", "SysEnd"),
                _ => null,
            })),
        };

    public Task<IReadOnlyList<ForeignKeyLookupItem>> LookupForeignKeyAsync(
        GridletConnectionContext context, string schema, string table, string keyColumn,
        string labelColumn, IReadOnlyList<object?> keys, string? search, int limit,
        CancellationToken cancellationToken = default)
    {
        ForeignKeyLookupItem[] pizzas =
        [
            new(1, "Margherita"), new(2, "Pepperoni"), new(3, "Hawaiian"), new(4, null),
            .. Enumerable.Range(5, 46).Select(id => new ForeignKeyLookupItem(id, $"Pizza {id}")),
        ];
        var keyTexts = keys.Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = search?.Trim();
        var browseAll = keyTexts.Count == 0 && string.IsNullOrEmpty(query);
        var matches = pizzas.Where(item => browseAll || keyTexts.Contains(Convert.ToString(
                item.Key, System.Globalization.CultureInfo.InvariantCulture)) ||
            (!string.IsNullOrEmpty(query) && Convert.ToString(
                item.Key, System.Globalization.CultureInfo.InvariantCulture) == query) ||
            (!string.IsNullOrEmpty(query) && query.Length >= 2 &&
             Convert.ToString(item.Label, System.Globalization.CultureInfo.InvariantCulture)?
                 .Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            .Take(limit)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ForeignKeyLookupItem>>(matches);
    }

    public Task<IReadOnlyList<object?>> GetDistinctColumnValuesAsync(
        GridletConnectionContext context, string schema, string table, string column,
        string? search, int limit, CancellationToken cancellationToken = default)
    {
        // Small deterministic sets so the UI can be exercised without a database.
        IReadOnlyList<object?> values = column.ToLowerInvariant() switch
        {
            "name" => new object?[] { "Ada", "Grace", "Edsger", "Alan" },
            "promotion" => new object?[] { "Featured", "Weekend", null },
            "status" => new object?[] { "Placed", "Preparing", "Ready", "OutForDelivery", "Delivered", "Cancelled" },
            "pizzaid" => new object?[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
            "id" => new object?[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
            _ => Array.Empty<object?>(),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmed = search.Trim();
            values = values.Where(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?
                .StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        }
        // For distribution (empty search, limit <=10) sample evenly across the set.
        if (string.IsNullOrWhiteSpace(search) && limit <= 10 && values.Count > limit)
        {
            var step = (double)values.Count / limit;
            var sampled = new List<object?>();
            for (var i = 0; i < limit; i++) sampled.Add(values[Math.Min(values.Count - 1, (int)(i * step))]);
            values = sampled;
        }
        return Task.FromResult<IReadOnlyList<object?>>(values.Take(limit).ToArray());
    }

    public Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context, string schema, string name, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(name switch
        {
            "AuditCustomers" =>
                $"CREATE TRIGGER {schema}.{name} ON {schema}.Customers AFTER INSERT AS SELECT 1;",
            "OrderNumbers" => $"CREATE SEQUENCE {schema}.{name} AS bigint START WITH 1000 INCREMENT BY 5;",
            _ => $"CREATE VIEW {schema}.{name} AS SELECT 1 AS One;",
        });

    public Task<string> GetUserDefinedTypeDefinitionAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
        => Task.FromResult(name == "OrderItems"
            ? $"CREATE TYPE [{schema}].[{name}] AS TABLE ([Id] int NOT NULL);"
            : $"CREATE TYPE [{schema}].[{name}] FROM nvarchar(32) NOT NULL;");

    public Task<SequenceInfo> GetSequenceAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SequenceInfo(
            new DbObjectInfo(schema, name, DbObjectType.Sequence,
                Description: "Order number generator"),
            "bigint", "1000", "5", long.MinValue.ToString(), long.MaxValue.ToString(),
            "1020", IsCycling: false, IsCached: true, CacheSize: 50));

    public Task CreateSequenceAsync(
        GridletConnectionContext context, SequenceDesign design,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"create sequence {design.Schema}.{design.Name}");
        return Task.CompletedTask;
    }

    public Task RestartSequenceAsync(
        GridletConnectionContext context, string schema, string name, string value,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"restart sequence {schema}.{name} {value}");
        return Task.CompletedTask;
    }

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

    /// <summary>Every page asked for, in order, so a caller's paging can be asserted.</summary>
    public List<(int Page, int PageSize)> DataPageRequests { get; } = [];

    public Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context, string schema, string name, TableDataRequest request,
        CancellationToken cancellationToken = default)
    {
        LastDataFilters = request.Filters;
        DataPageRequests.Add((request.Page, request.PageSize));
        return GetPageCore(name, request);
    }

    /// <summary>
    /// Four rows served a page at a time, from a table that can be addressed and one that cannot.
    /// A caller that pages through the second one is reading an unordered table twice.
    /// </summary>
    private static TableDataPage LedgerPage(string name, TableDataRequest request)
    {
        object?[][] all =
        [
            [1, "Ada", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)],
            [2, "Grace", new DateTime(2026, 1, 2), new DateTime(2026, 2, 2)],
            [3, "Edsger", new DateTime(2026, 1, 3), new DateTime(2026, 2, 3)],
            [4, "Alan", new DateTime(2026, 1, 4), new DateTime(2026, 2, 4)],
        ];
        var taken = all
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToArray();
        return new TableDataPage(
            [new ResultColumn("Id", "int"), new ResultColumn("Name", "nvarchar(100)"),
                new ResultColumn("SysStart", "datetime2"), new ResultColumn("SysEnd", "datetime2")],
            taken,
            request.Page,
            request.PageSize,
            TotalRows: all.Length,
            RowIdentity: name == "LedgerHeap"
                ? null
                : new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]));
    }

    private static Task<TableDataPage> GetPageCore(string name, TableDataRequest request)
    {
        if (name is "Ledger" or "LedgerHeap")
        {
            return Task.FromResult(LedgerPage(name, request));
        }

        return GetFixedPage(name, request);
    }

    private static Task<TableDataPage> GetFixedPage(string name, TableDataRequest request)
        => Task.FromResult(name == "Orders"
            ? new TableDataPage(
                [new ResultColumn("Id", "int"), new ResultColumn("PizzaId", "int"),
                    new ResultColumn("Promotion", "nvarchar(100)")],
                [[10, 1, "Featured"], [11, 4, null], [12, 99, "Weekend"]],
                request.Page, request.PageSize, TotalRows: 3,
                RowIdentity: new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]),
                RowKeys: [[10], [11], [12]])
            : name == "Pizzas"
            ? new TableDataPage(
                [new ResultColumn("Id", "int"), new ResultColumn("Name", "nvarchar(100)")],
                [[1, "{\"person\":{\"name\":\"Ada\",\"active\":true},\"scores\":[3,5,8]}"],
                    [2, "{not valid JSON}"]],
                request.Page,
                request.PageSize,
                TotalRows: 2,
                RowIdentity: new RowIdentityInfo(RowIdentityKinds.PrimaryKey, ["Id"]),
                RowKeys: [[1], [2]])
            : name == "Heap"
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
        LastWriteValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        Calls.Add($"insert {schema}.{table} ({string.Join(",", values.Keys)})");
        return Task.FromResult(1);
    }

    public Task<int> UpdateRowAsync(
        GridletConnectionContext context, string schema, string table,
        IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        LastWriteValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
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

    public Task AddDefaultConstraintAsync(
        GridletConnectionContext context, string schema, string table, DefaultConstraintDesign defaultConstraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"addDefaultConstraint {schema}.{table}.{defaultConstraint.Column}.{defaultConstraint.Name ?? "(unnamed)"} expression={defaultConstraint.Expression}");
        return Task.CompletedTask;
    }

    public Task DropDefaultConstraintAsync(
        GridletConnectionContext context, string schema, string table, ConstraintReference constraint,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"dropDefaultConstraint {schema}.{table}.{constraint.Name ?? $"#{constraint.Ordinal}"}");
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

    public string BuildDropScript(DbObjectInfo @object)
        => $"DROP {@object.Type.ToString().ToUpperInvariant()} {@object.Schema}.{@object.Name};";

    public string BuildInsertScript(
        TableDefinition table, IReadOnlyList<ResultColumn> columns, IReadOnlyList<object?[]> rows)
        => string.Join('\n', rows.Select(row =>
            $"INSERT INTO {table.Object.Schema}.{table.Object.Name} "
            + $"({string.Join(", ", columns.Select(column => column.Name))}) "
            + $"VALUES ({string.Join(", ", row.Select(value => value ?? "NULL"))});"));

    public Task RenameObjectAsync(
        GridletConnectionContext context, string schema, string name, DbObjectType type, string newName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"renameObject {type} {schema}.{name} -> {newName}");
        return Task.CompletedTask;
    }

    public Task RenameIndexAsync(
        GridletConnectionContext context, string schema, string table, string indexName, string newName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"renameIndex {schema}.{table}.{indexName} -> {newName}");
        return Task.CompletedTask;
    }

    public Task TruncateTableAsync(
        GridletConnectionContext context, string schema, string table,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"truncate {schema}.{table}");
        return Task.CompletedTask;
    }

    public Task<TableImportResult> ImportAsync(
        GridletConnectionContext context, string schema, string table, TableImport import,
        CancellationToken cancellationToken = default)
    {
        LastImport = import;
        Calls.Add($"import {schema}.{table} {import.Rows.Count}");
        return Task.FromResult(new TableImportResult(import.Rows.Count));
    }
}
