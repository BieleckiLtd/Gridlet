using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteProviderTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();
        context = new GridletConnectionContext(new GridletConnectionOptions
        {
            Name = "Test",
            ConnectionString = connectionString,
            ProviderName = GridletProviderNames.Sqlite,
        }, "main");

        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Email TEXT,
                DisplayName AS (Name || ' <' || Email || '>') STORED
            );
            CREATE UNIQUE INDEX UX_Customers_Email ON Customers (Email);
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Total NUMERIC NOT NULL DEFAULT (0),
                CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES Customers (Id) ON DELETE CASCADE
            );
            CREATE TABLE CustomerAudit (
                AuditId INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Action TEXT NOT NULL
            );
            CREATE TRIGGER AuditCustomerInsert
            AFTER INSERT ON Customers
            BEGIN
                INSERT INTO CustomerAudit (CustomerId, Action) VALUES (NEW.Id, 'INSERT');
            END;
            CREATE VIEW CustomerNames AS SELECT Id, Name FROM Customers;
            INSERT INTO Customers (Name, Email) VALUES ('Ada', 'ada@example.com'), ('Grace', 'grace@example.com');
            INSERT INTO Orders (CustomerId, Total) VALUES (1, 12.5);
            """,
            new QueryRequestOptions(100, 30));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public void Advertises_sqlite_ui_capabilities()
    {
        Assert.Equal("main", provider.Capabilities.DefaultSchema);
        Assert.True(provider.Capabilities.SupportsViews);
        Assert.False(provider.Capabilities.SupportsSchemas);
        Assert.False(provider.Capabilities.SupportsStoredProcedures);
        Assert.False(provider.Capabilities.SupportsFunctions);
        Assert.True(provider.Capabilities.SupportsTriggers);
        Assert.False(provider.Capabilities.SupportsClusteredPrimaryKeys);
        Assert.Contains("LIMIT 100", provider.Capabilities.SelectExample);
        Assert.Equal("Recreate", provider.Capabilities.ObjectEditMode);
    }

    [Fact]
    public async Task Reads_database_objects_columns_indexes_foreign_keys_and_definitions()
    {
        Assert.Equal([new DatabaseInfo("main", false)], await provider.Schema.GetDatabasesAsync(context));
        Assert.Equal([new SchemaInfo("main", "")], await provider.Schema.GetSchemasAsync(context));

        var objects = await provider.Schema.GetObjectsAsync(context);
        Assert.Contains(new DbObjectInfo("main", "Customers", DbObjectType.Table), objects);
        Assert.Contains(new DbObjectInfo("main", "CustomerNames", DbObjectType.View), objects);
        Assert.Contains(new DbObjectInfo("main", "AuditCustomerInsert", DbObjectType.Trigger), objects);
        Assert.DoesNotContain(objects, item => item.Name.StartsWith("sqlite_", StringComparison.Ordinal));

        var customers = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        Assert.True(customers.Columns.Single(c => c.Name == "Id").IsIdentity);
        var computed = customers.Columns.Single(c => c.Name == "DisplayName");
        Assert.True(computed.IsComputed);
        Assert.True(computed.IsPersisted);
        Assert.Equal("Name || ' <' || Email || '>'", computed.ComputedDefinition);
        Assert.Contains(customers.Indexes, i => i.Name == "UX_Customers_Email" && i.IsUnique);

        var orders = await provider.Schema.GetTableDefinitionAsync(context, "main", "Orders");
        var foreignKey = Assert.Single(orders.ForeignKeys);
        Assert.Equal("Customers", foreignKey.ReferencedTable);
        Assert.Equal(new ForeignKeyColumnPair("CustomerId", "Id"), Assert.Single(foreignKey.Columns));
        Assert.Equal("CASCADE", foreignKey.OnDelete);

        var viewSql = await provider.Schema.GetObjectDefinitionAsync(context, "main", "CustomerNames");
        Assert.Contains("CREATE VIEW CustomerNames", viewSql, StringComparison.OrdinalIgnoreCase);
        var triggerSql = await provider.Schema.GetObjectDefinitionAsync(context, "main", "AuditCustomerInsert");
        Assert.Contains("CREATE TRIGGER AuditCustomerInsert", triggerSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_data_executes_parameters_and_streams_with_a_cap()
    {
        var page = await provider.Data.GetPageAsync(context, "main", "Customers",
            new TableDataRequest(1, 1, "Name", SortDirection.Descending));
        Assert.Equal(2, page.TotalRows);
        Assert.Single(page.Rows);
        Assert.Equal("Grace", page.Rows[0][1]);

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Name FROM Customers WHERE Id = @id;",
            new QueryRequestOptions(10, 30),
            new Dictionary<string, object?> { ["id"] = 1L });
        Assert.Equal("Ada", Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);

        var events = new List<QueryStreamEvent>();
        await foreach (var item in provider.Query.StreamAsync(context,
            "SELECT Id FROM Customers ORDER BY Id;", new QueryRequestOptions(1, 30)))
        {
            events.Add(item);
        }
        Assert.True(events.Single(e => e.Type == "resultSetCompleted").Truncated);
        Assert.Single(events.Single(e => e.Type == "rows").Rows!);
    }

    [Fact]
    public async Task Inserts_updates_and_deletes_rows_with_metadata_validation()
    {
        Assert.Equal(1, await provider.Writes.InsertRowAsync(context, "main", "Customers",
            new Dictionary<string, object?> { ["Name"] = "Linus", ["Email"] = null }));
        Assert.Equal(1, await provider.Writes.UpdateRowAsync(context, "main", "Customers",
            new Dictionary<string, object?> { ["Id"] = 3L },
            new Dictionary<string, object?> { ["Email"] = "linus@example.com" }));
        Assert.Equal(1, await provider.Writes.DeleteRowAsync(context, "main", "Customers",
            new Dictionary<string, object?> { ["Id"] = 3L }));

        var audit = await provider.Query.ExecuteAsync(context,
            "SELECT Action FROM CustomerAudit WHERE CustomerId = 3;", new QueryRequestOptions(10, 30));
        Assert.Equal("INSERT", Assert.Single(Assert.Single(audit.ResultSets).Rows)[0]);

        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.InsertRowAsync(
            context, "main", "Customers", new Dictionary<string, object?> { ["Id"] = 10L }));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.UpdateRowAsync(
            context, "main", "Customers", new Dictionary<string, object?> { ["Id"] = 1L },
            new Dictionary<string, object?> { ["Unknown"] = "x" }));
    }

    [Fact]
    public async Task Drops_triggers_as_first_class_objects()
    {
        await provider.Ddl.DropObjectAsync(
            context, "main", "AuditCustomerInsert", DbObjectType.Trigger);

        Assert.DoesNotContain(await provider.Schema.GetObjectsAsync(context),
            o => o.Type == DbObjectType.Trigger && o.Name == "AuditCustomerInsert");
    }

    [Fact]
    public async Task Recreates_trigger_definitions_transactionally_for_edits()
    {
        await Assert.ThrowsAsync<GridletQueryException>(async () =>
        {
            await foreach (var _ in provider.Query.StreamAsync(context,
                """
                BEGIN IMMEDIATE;
                DROP TRIGGER IF EXISTS [main].[AuditCustomerInsert];
                CREATE TRIGGER this is not valid SQL;
                COMMIT;
                """,
                new QueryRequestOptions(10, 30)))
            {
            }
        });
        Assert.NotNull(await provider.Schema.GetObjectDefinitionAsync(
            context, "main", "AuditCustomerInsert"));

        await provider.Query.ExecuteAsync(context,
            """
            BEGIN IMMEDIATE;
            DROP TRIGGER IF EXISTS [main].[AuditCustomerInsert];
            CREATE TRIGGER AuditCustomerInsert
            AFTER INSERT ON Customers
            BEGIN
                INSERT INTO CustomerAudit (CustomerId, Action) VALUES (NEW.Id, 'EDITED');
            END;
            COMMIT;
            INSERT INTO Customers (Name, Email) VALUES ('Linus', 'linus@example.com');
            """,
            new QueryRequestOptions(10, 30));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Action FROM CustomerAudit WHERE CustomerId = 3;", new QueryRequestOptions(10, 30));
        Assert.Equal("EDITED", Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);
    }

    [Fact]
    public async Task Covers_table_column_key_and_object_ddl_while_preserving_data_and_indexes()
    {
        await provider.Ddl.CreateTableAsync(context, new TableDesign("main", "Notes",
        [
            new ColumnDesign("Code", "TEXT", IsNullable: false),
            new ColumnDesign("Body", "TEXT"),
        ]));
        await provider.Writes.InsertRowAsync(context, "main", "Notes",
            new Dictionary<string, object?> { ["Code"] = "n1", ["Body"] = "hello" });
        await provider.Query.ExecuteAsync(context, "CREATE UNIQUE INDEX UX_Notes_Body ON Notes (Body);",
            new QueryRequestOptions(10, 30));
        await provider.Ddl.AddColumnAsync(context, "main", "Notes",
            new ColumnDesign("Priority", "INTEGER", IsNullable: false, DefaultExpression: "1"));
        await provider.Ddl.AlterColumnAsync(context, "main", "Notes", "Body",
            new ColumnDesign("Text", "VARCHAR(200)"));
        await provider.Ddl.AddPrimaryKeyAsync(context, "main", "Notes",
            new PrimaryKeyDesign("PK_Notes", ["Code"]));
        await provider.Ddl.AddForeignKeyAsync(context, "main", "Notes",
            new ForeignKeyDesign("FK_Notes_Customers", "main", "Customers",
                [new ForeignKeyColumnPair("Priority", "Id")]));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Notes");
        Assert.True(definition.Columns.Single(c => c.Name == "Code").IsPrimaryKey);
        Assert.Contains(definition.Columns, c => c.Name == "Text" && c.DataType == "VARCHAR(200)");
        Assert.Contains(definition.Indexes,
            i => i.Name == "UX_Notes_Body" && i.IsUnique && i.Columns.SequenceEqual(["Text"]));
        Assert.Single(definition.ForeignKeys);
        var page = await provider.Data.GetPageAsync(context, "main", "Notes", new TableDataRequest(1, 10));
        Assert.Equal("hello", page.Rows[0][1]);

        await provider.Ddl.DropConstraintAsync(context, "main", "Notes", Assert.Single(definition.ForeignKeys).Name);
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Notes");
        await provider.Ddl.DropConstraintAsync(context, "main", "Notes",
            definition.Indexes.Single(i => i.IsPrimaryKey).Name);
        await provider.Ddl.DropColumnAsync(context, "main", "Notes", "Priority");
        await provider.Query.ExecuteAsync(context,
            "CREATE VIEW NotesView AS SELECT Code, Text FROM Notes;", new QueryRequestOptions(10, 30));
        await provider.Ddl.DropObjectAsync(context, "main", "NotesView", DbObjectType.View);
        await provider.Ddl.DropTableAsync(context, "main", "Notes");

        Assert.DoesNotContain(await provider.Schema.GetObjectsAsync(context), o => o.Name is "Notes" or "NotesView");
    }

    [Fact]
    public async Task Rejects_unsupported_schema_operations()
    {
        await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.CreateSchemaAsync(context, new SchemaDesign("other")));
        await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Schema.GetTableDefinitionAsync(context, "other", "Customers"));
    }
}
