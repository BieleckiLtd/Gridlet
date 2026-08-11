using Gridlet.Abstractions;
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
            Pooling = false,
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
        Assert.True(provider.Capabilities.SupportsCheckConstraints);
        Assert.True(provider.Capabilities.SupportsUniqueConstraints);
        Assert.True(provider.Capabilities.SupportsIndexes);
    }

    [Fact]
    public async Task Reports_sqlite_technology_and_engine_version()
    {
        var infoProvider = Assert.IsAssignableFrom<IGridletDatabaseSystemInfoProvider>(provider);

        var info = await infoProvider.GetDatabaseSystemInfoAsync(context);

        Assert.Equal("SQLite", info.Technology);
        Assert.NotNull(info.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", info.Version);
    }

    [Fact]
    public async Task Foreign_key_lookup_resolves_keys_and_searches_labels()
    {
        var lookup = Assert.IsAssignableFrom<IForeignKeyLookupProvider>(provider);

        var keys = await lookup.LookupForeignKeyAsync(
            context, "main", "Customers", "Id", "Name", [2L], null, 50);
        var grace = Assert.Single(keys);
        Assert.Equal(2L, grace.Key);
        Assert.Equal("Grace", grace.Label);

        var search = await lookup.LookupForeignKeyAsync(
            context, "main", "Customers", "Id", "Name", [], "ad", 50);
        var ada = Assert.Single(search);
        Assert.Equal("Ada", ada.Label);

        var oneCharacterLabelSearch = await lookup.LookupForeignKeyAsync(
            context, "main", "Customers", "Id", "Name", [], "G", 50);
        Assert.Empty(oneCharacterLabelSearch);

        var browsed = await lookup.LookupForeignKeyAsync(
            context, "main", "Customers", "Id", "Name", [], null, 50);
        Assert.Equal(["Ada", "Grace"], browsed.Select(item => item.Label));
    }

    [Fact]
    public async Task Reads_database_objects_columns_indexes_foreign_keys_and_definitions()
    {
        Assert.Equal([new DatabaseInfo("main", false)],
            await provider.Schema.GetDatabasesAsync(context));
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
    public async Task Canonicalizes_case_variant_selected_database_names()
    {
        var caseVariant = context with { Database = "MAIN" };

        var objects = await provider.Schema.GetObjectsAsync(caseVariant);
        Assert.Contains(objects, item => item.Name == "Customers" && item.Schema == "main");
        var page = await provider.Data.GetPageAsync(
            caseVariant, "main", "Customers", new TableDataRequest(1, 10));
        Assert.Equal(2, page.TotalRows);

        var routeError = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Data.GetPageAsync(caseVariant, "MAIN", "Customers", new TableDataRequest(1, 10)));
        Assert.Contains("does not contain schema", routeError.Message);
    }

    [Fact]
    public async Task Infers_incoming_and_outgoing_object_dependencies()
    {
        var incoming = await provider.Schema.GetObjectDependenciesAsync(context, "main", "Customers");
        Assert.Contains(incoming, dependency => dependency.Direction == "referencedBy" &&
            dependency.Object.Name == "CustomerNames" && dependency.IsInferred);
        Assert.Contains(incoming, dependency => dependency.Direction == "referencedBy" &&
            dependency.Object.Name == "Orders" && dependency.IsInferred);

        var outgoing = await provider.Schema.GetObjectDependenciesAsync(context, "main", "CustomerNames");
        Assert.Contains(outgoing, dependency => dependency.Direction == "references" &&
            dependency.Object.Name == "Customers");
    }

    [Fact]
    public async Task Host_configured_attachments_are_isolated_browsable_and_writable()
    {
        var attachedPath = Path.Combine(Path.GetTempPath(), $"gridlet-attached-{Guid.NewGuid():N}.db");
        try
        {
            var pooledConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = true,
                ForeignKeys = true,
            }.ToString();
            var options = new GridletConnectionOptions
            {
                Name = "Pooled",
                ConnectionString = pooledConnectionString,
                ProviderName = GridletProviderNames.Sqlite,
                SqliteAttachments = new() { ["archive"] = attachedPath },
            };
            var pooledContext = new GridletConnectionContext(options, "main");
            var databases = await provider.Schema.GetDatabasesAsync(pooledContext);
            Assert.DoesNotContain(databases, database => database.Name == "temp");
            Assert.Contains(new DatabaseInfo("archive", false), databases);
            // A second pooled open must reuse the existing attachment instead of issuing ATTACH again.
            Assert.Contains(new DatabaseInfo("archive", false),
                await provider.Schema.GetDatabasesAsync(pooledContext));
            var isolatedContext = new GridletConnectionContext(new GridletConnectionOptions
            {
                Name = "SamePoolWithoutAttachments",
                ConnectionString = pooledConnectionString,
                ProviderName = GridletProviderNames.Sqlite,
            }, "main");
            Assert.DoesNotContain(new DatabaseInfo("archive", false),
                await provider.Schema.GetDatabasesAsync(isolatedContext));
            // Reopening the configured context must restore its own allowlist after pool reuse.
            Assert.Contains(new DatabaseInfo("archive", false),
                await provider.Schema.GetDatabasesAsync(pooledContext));

            var archiveContext = pooledContext with { Database = "archive" };
            await provider.Ddl.CreateTableAsync(archiveContext, new TableDesign("archive", "Notes",
            [
                new ColumnDesign("Id", "INTEGER", IsNullable: false, IsPrimaryKey: true),
                new ColumnDesign("Body", "TEXT", IsNullable: false),
                new ColumnDesign("Obsolete", "TEXT", IsNullable: true),
            ]));
            await provider.Query.ExecuteAsync(archiveContext,
                """
                CREATE TABLE archive.NoteAudit (NoteId INTEGER NOT NULL);
                CREATE TRIGGER "archive"."AuditNote" AFTER INSERT ON Notes
                BEGIN
                    INSERT INTO NoteAudit (NoteId) VALUES (NEW.Id);
                END;
                """, new QueryRequestOptions(100, 30));
            await provider.Ddl.DropColumnAsync(archiveContext, "archive", "Notes", "Obsolete");
            await provider.Writes.InsertRowAsync(archiveContext, "archive", "Notes",
                new Dictionary<string, object?> { ["Id"] = 1L, ["Body"] = "attached" });

            Assert.Contains(new DbObjectInfo("archive", "Notes", DbObjectType.Table),
                await provider.Schema.GetObjectsAsync(archiveContext));
            var page = await provider.Data.GetPageAsync(
                archiveContext, "archive", "Notes", new TableDataRequest(1, 10));
            Assert.Equal("attached", Assert.Single(page.Rows)[1]);
            var audit = await provider.Query.ExecuteAsync(archiveContext,
                "SELECT COUNT(*) FROM archive.NoteAudit WHERE NoteId = 1;",
                new QueryRequestOptions(10, 30));
            Assert.Equal(1L, Assert.Single(Assert.Single(audit.ResultSets).Rows)[0]);

            var mismatch = await Assert.ThrowsAsync<GridletValidationException>(() =>
                provider.Ddl.CreateTableAsync(archiveContext,
                    new TableDesign("main", "WrongDatabase", [new ColumnDesign("Id", "INTEGER")])));
            Assert.Contains("does not contain schema", mismatch.Message);

            var tempContext = pooledContext with { Database = "temp" };
            var tempError = await Assert.ThrowsAsync<GridletValidationException>(() =>
                provider.Schema.GetObjectsAsync(tempContext));
            Assert.Contains("does not contain database 'temp'", tempError.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(attachedPath)) File.Delete(attachedPath);
        }
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
    public async Task Import_is_atomic_when_a_later_row_fails()
    {
        var importer = Assert.IsAssignableFrom<ITableImportProvider>(provider);
        var rows = Enumerable.Range(0, 450)
            .Select(index => (IReadOnlyList<object?>)[$"Imported {index}", $"imported-{index}@example.com"])
            .ToList();
        // This lands in a second statement batch and conflicts with the first row.
        rows.Add(["Duplicate", "imported-0@example.com"]);
        var import = new TableImport(
            ["Name", "Email"],
            rows);

        await Assert.ThrowsAsync<GridletQueryException>(() =>
            importer.ImportAsync(context, "main", "Customers", import));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT COUNT(*) AS Count FROM Customers WHERE Email LIKE 'imported-%';",
            new QueryRequestOptions(10, 30));
        Assert.Equal(0L, Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);
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

    [Fact]
    public async Task Rebuilding_a_parent_table_does_not_cascade_delete_child_rows()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE RebuildParents (Id INTEGER PRIMARY KEY, Label TEXT);
            CREATE TABLE RebuildChildren (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL REFERENCES RebuildParents(Id) ON DELETE CASCADE
            );
            INSERT INTO RebuildParents VALUES (1, 'before');
            INSERT INTO RebuildChildren VALUES (1, 1);
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "RebuildParents", "Label",
            new ColumnDesign("Description", "TEXT"));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT COUNT(*) FROM RebuildChildren;", new QueryRequestOptions(10, 30));
        Assert.Equal(1L, Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);

        // The connection-level pragma is restored after the rebuild.
        await Assert.ThrowsAsync<GridletQueryException>(() => provider.Query.ExecuteAsync(context,
            "INSERT INTO RebuildChildren VALUES (2, 999);", new QueryRequestOptions(10, 30)));
    }

    [Fact]
    public async Task Rebuild_preserves_the_high_water_mark_for_autoincrement_tables()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE SequenceItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL
            );
            INSERT INTO SequenceItems (Name) VALUES ('kept');
            INSERT INTO SequenceItems (Id, Name) VALUES (100, 'removed');
            DELETE FROM SequenceItems WHERE Id = 100;
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "SequenceItems", "Name",
            new ColumnDesign("Description", "TEXT", IsNullable: false));
        await provider.Query.ExecuteAsync(context,
            "INSERT INTO SequenceItems (Description) VALUES ('after rebuild');",
            new QueryRequestOptions(10, 30));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Id FROM SequenceItems WHERE Description = 'after rebuild';",
            new QueryRequestOptions(10, 30));

        Assert.Equal(101L, Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);
    }

    [Fact]
    public async Task Rebuilds_keep_check_constraints_strict_and_without_rowid()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE Guarded (
                Id INTEGER PRIMARY KEY,
                Value INTEGER CHECK (Value > 0)
            ) STRICT;
            CREATE TABLE Compact (
                Code TEXT PRIMARY KEY,
                Value TEXT
            ) WITHOUT ROWID;
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "Guarded", "Value",
            new ColumnDesign("RenamedValue", "INTEGER"));
        await provider.Ddl.AlterColumnAsync(context, "main", "Compact", "Value",
            new ColumnDesign("RenamedValue", "TEXT"));

        // What the table is has to survive the rebuild, not just what it contains.
        var guarded = await provider.Schema.GetObjectDefinitionAsync(context, "main", "Guarded");
        var compact = await provider.Schema.GetObjectDefinitionAsync(context, "main", "Compact");
        Assert.Contains("CHECK", guarded);
        Assert.Contains("STRICT", guarded);
        Assert.Contains("RenamedValue", guarded);
        Assert.Contains("WITHOUT ROWID", compact);
        Assert.Contains("RenamedValue", compact);
    }

    [Fact]
    public async Task Refuses_rebuilds_that_would_drop_on_conflict_policies()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE ConflictItems (
                Id INTEGER PRIMARY KEY,
                Code TEXT UNIQUE ON CONFLICT IGNORE,
                Description TEXT
            );
            INSERT INTO ConflictItems VALUES (1, 'same', 'first');
            INSERT INTO ConflictItems VALUES (2, 'same', 'ignored before rebuild');
            """,
            new QueryRequestOptions(10, 30));
        var originalSql = await provider.Schema.GetObjectDefinitionAsync(
            context, "main", "ConflictItems");

        var exception = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.AlterColumnAsync(context, "main", "ConflictItems", "Description",
                new ColumnDesign("Details", "TEXT")));

        Assert.Contains("ON CONFLICT", exception.Message);
        Assert.Equal(originalSql,
            await provider.Schema.GetObjectDefinitionAsync(context, "main", "ConflictItems"));
        await provider.Query.ExecuteAsync(context,
            "INSERT INTO ConflictItems VALUES (3, 'same', 'ignored after refusal');",
            new QueryRequestOptions(10, 30));
        var result = await provider.Query.ExecuteAsync(context,
            "SELECT COUNT(*) FROM ConflictItems;", new QueryRequestOptions(10, 30));
        Assert.Equal(1L, Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);
    }

    [Fact]
    public async Task Preserves_index_direction_and_collation_during_rebuild()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE IndexedValues (Id INTEGER PRIMARY KEY, Name TEXT, Other TEXT);
            CREATE INDEX IX_IndexedValues_Name ON IndexedValues (Name COLLATE NOCASE DESC);
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "IndexedValues", "Other",
            new ColumnDesign("Description", "TEXT"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "IndexedValues");
        var index = Assert.Single(definition.Indexes, item => item.Name == "IX_IndexedValues_Name");
        var key = Assert.Single(index.KeyColumns!);
        Assert.True(key.IsDescending);
        Assert.Equal("NOCASE", key.Collation);
        Assert.Contains("Description", await provider.Schema.GetObjectDefinitionAsync(context, "main", "IndexedValues"));
    }

    [Fact]
    public async Task Classifies_fts5_virtual_and_shadow_tables_and_refuses_shadow_writes_or_rebuilds()
    {
        await provider.Query.ExecuteAsync(context,
            "CREATE VIRTUAL TABLE SearchDocs USING fts5(Body); INSERT INTO SearchDocs (Body) VALUES ('keep me');",
            new QueryRequestOptions(10, 30));

        var objects = await provider.Schema.GetObjectsAsync(context);
        var virtualTable = Assert.Single(objects, item => item.Name == "SearchDocs");
        Assert.Equal("virtual", virtualTable.SubKind);
        Assert.False(virtualTable.IsInternal);
        var shadow = Assert.Single(objects, item => item.Name == "SearchDocs_data");
        Assert.Equal("shadow", shadow.SubKind);
        Assert.True(shadow.IsInternal);

        var ftsDefinition = await provider.Schema.GetTableDefinitionAsync(context, "main", "SearchDocs");
        Assert.Contains(ftsDefinition.Columns, column => column.IsHidden);
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.InsertRowAsync(
            context, "main", "SearchDocs_data", new Dictionary<string, object?> { ["id"] = 99L }));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.UpdateRowAsync(
            context, "main", "SearchDocs_data", new Dictionary<string, object?> { ["id"] = 1L },
            new Dictionary<string, object?> { ["block"] = Array.Empty<byte>() }));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.DeleteRowAsync(
            context, "main", "SearchDocs_data", new Dictionary<string, object?> { ["id"] = 1L }));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Writes.UpdateRowAsync(
            context, "main", "sqlite_sequence", new Dictionary<string, object?> { ["name"] = "Customers" },
            new Dictionary<string, object?> { ["seq"] = 999L }));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Ddl.AlterColumnAsync(
            context, "main", "SearchDocs", "Body", new ColumnDesign("Text", "TEXT")));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Ddl.AlterColumnAsync(
            context, "main", "SearchDocs_data", "block", new ColumnDesign("payload", "BLOB")));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Ddl.DropTableAsync(
            context, "main", "SearchDocs_data"));
        await Assert.ThrowsAsync<GridletValidationException>(() => provider.Ddl.DropObjectAsync(
            context, "main", "SearchDocs_data", DbObjectType.Table));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Body FROM SearchDocs WHERE SearchDocs MATCH 'keep';", new QueryRequestOptions(10, 30));
        Assert.Equal("keep me", Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);

        await provider.Ddl.DropTableAsync(context, "main", "SearchDocs");
        Assert.DoesNotContain(await provider.Schema.GetObjectsAsync(context), item =>
            item.Name.StartsWith("SearchDocs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parses_single_quoted_sqlite_identifiers_for_constraints()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE 'Single Quoted' (
                'Id' INTEGER PRIMARY KEY,
                'Value' TEXT,
                CONSTRAINT 'CK single' CHECK ("Value" <> ''),
                CONSTRAINT 'UQ single' UNIQUE ('Value')
            );
            INSERT INTO 'Single Quoted' VALUES (1, 'kept');
            """,
            new QueryRequestOptions(10, 30));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Single Quoted");
        Assert.Contains(definition.CheckConstraints, check => check.Name == "CK single");
        Assert.Contains(definition.UniqueConstraints, unique => unique.Name == "UQ single" &&
            unique.Columns.Single().Column == "Value");

        await provider.Ddl.AlterColumnAsync(context, "main", "Single Quoted", "Value",
            new ColumnDesign("Text", "TEXT"));
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Single Quoted");
        Assert.Contains(definition.CheckConstraints, check => check.Name == "CK single" &&
            check.Definition.Contains("Text", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(definition.UniqueConstraints, unique => unique.Name == "UQ single" &&
            unique.Columns.Single().Column == "Text");
    }

    [Fact]
    public async Task Renaming_a_column_updates_other_generated_column_expressions()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE GeneratedRename (
                Id INTEGER PRIMARY KEY,
                Source TEXT,
                Derived TEXT AS (upper(Source) || ':' || Source) STORED
            );
            INSERT INTO GeneratedRename (Id, Source) VALUES (1, 'value');
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "GeneratedRename", "Source",
            new ColumnDesign("Input", "TEXT"));
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "GeneratedRename");
        var generated = definition.Columns.Single(column => column.Name == "Derived");
        Assert.Contains("Input", generated.ComputedDefinition);
        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Input, Derived FROM GeneratedRename;", new QueryRequestOptions(10, 30));
        Assert.Equal(["value", "VALUE:value"], Assert.Single(Assert.Single(result.ResultSets).Rows));
    }

    [Fact]
    public async Task Renaming_a_column_does_not_rename_same_named_index_functions()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE FunctionNames (Id INTEGER PRIMARY KEY, lower TEXT, Name TEXT);
            CREATE INDEX IX_FunctionNames_Mixed ON FunctionNames (lower, lower(Name));
            INSERT INTO FunctionNames VALUES (1, 'column', 'Name');
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "FunctionNames", "lower",
            new ColumnDesign("LowerValue", "TEXT"));
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "FunctionNames");
        var keys = definition.Indexes.Single(index => index.Name == "IX_FunctionNames_Mixed").KeyColumns!;
        Assert.Equal("LowerValue", keys[0].Column);
        Assert.Equal("lower(Name)", keys[1].Expression);
    }

    [Fact]
    public async Task Models_and_manages_named_and_unnamed_check_and_unique_constraints()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE ConstrainedValues (
                Id INTEGER PRIMARY KEY,
                Code TEXT CONSTRAINT UQ_Constrained_Code UNIQUE,
                Alternative TEXT UNIQUE,
                Score INTEGER CONSTRAINT CK_Constrained_Score CHECK (Score /* nested syntax: ), */ > 0 AND instr('a,b', ',') > 0),
                /* a comma and nested calls must not split this constraint */
                CONSTRAINT CK_Constrained_Total CHECK ((Score + length(coalesce(Alternative, ''))) < 100)
            );
            INSERT INTO ConstrainedValues VALUES (1, 'one', 'alt', 2);
            """,
            new QueryRequestOptions(10, 30));

        var before = await provider.Schema.GetTableDefinitionAsync(context, "main", "ConstrainedValues");
        Assert.Equal(2, before.CheckConstraints.Count);
        Assert.Contains(before.CheckConstraints, check => check.Name == "CK_Constrained_Score" && check.Column == "Score");
        Assert.Equal(2, before.UniqueConstraints.Count);
        Assert.Contains(before.UniqueConstraints, unique => unique.Name == "UQ_Constrained_Code");
        Assert.Contains(before.UniqueConstraints, unique => unique.Name is null);
        Assert.DoesNotContain(before.Indexes, index => index.Name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal));

        await provider.Ddl.AlterColumnAsync(context, "main", "ConstrainedValues", "Score",
            new ColumnDesign("Points", "INTEGER"));
        var rebuilt = await provider.Schema.GetTableDefinitionAsync(context, "main", "ConstrainedValues");
        Assert.Contains(rebuilt.CheckConstraints, check => check.Name == "CK_Constrained_Score" &&
            check.Definition.Contains("Points", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rebuilt.UniqueConstraints, unique => unique.Name == "UQ_Constrained_Code");
        Assert.Contains(rebuilt.UniqueConstraints, unique => unique.Name is null);
        Assert.Contains("CONSTRAINT \"UQ_Constrained_Code\" UNIQUE", await provider.Schema.GetObjectDefinitionAsync(
            context, "main", "ConstrainedValues"), StringComparison.OrdinalIgnoreCase);

        await provider.Ddl.AddCheckConstraintAsync(context, "main", "ConstrainedValues",
            new CheckConstraintDesign(null, "Points < 50"));
        rebuilt = await provider.Schema.GetTableDefinitionAsync(context, "main", "ConstrainedValues");
        var addedCheck = rebuilt.CheckConstraints.Single(check => check.Name is null);
        await provider.Ddl.DropCheckConstraintAsync(context, "main", "ConstrainedValues",
            new ConstraintReference(Ordinal: addedCheck.Ordinal));

        await provider.Ddl.AddUniqueConstraintAsync(context, "main", "ConstrainedValues",
            new UniqueConstraintDesign("UQ_Constrained_Pair",
                [new IndexKeyDesign("Code"), new IndexKeyDesign("Alternative", IsDescending: true)]));
        rebuilt = await provider.Schema.GetTableDefinitionAsync(context, "main", "ConstrainedValues");
        Assert.Contains(rebuilt.UniqueConstraints, unique => unique.Name == "UQ_Constrained_Pair" &&
            unique.Columns[1].IsDescending);
        await provider.Ddl.DropUniqueConstraintAsync(context, "main", "ConstrainedValues",
            new ConstraintReference("UQ_Constrained_Pair"));

        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Code, Alternative, Points FROM ConstrainedValues;", new QueryRequestOptions(10, 30));
        Assert.Equal(["one", "alt", 2L], Assert.Single(Assert.Single(result.ResultSets).Rows));
    }

    [Fact]
    public async Task Loads_rebuilds_creates_and_drops_rich_ordinary_indexes()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE RichIndexes (Id INTEGER PRIMARY KEY, Name TEXT, Other TEXT);
            CREATE UNIQUE INDEX IX_Rich_Expression ON RichIndexes
                (lower(/* nested: ), */ Name) COLLATE NOCASE DESC, Other ASC)
                WHERE Name IS NOT NULL AND Other <> 'x,y';
            INSERT INTO RichIndexes VALUES (1, 'One', 'kept');
            """,
            new QueryRequestOptions(10, 30));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "RichIndexes");
        var rich = Assert.Single(definition.Indexes, index => index.Name == "IX_Rich_Expression");
        Assert.True(rich.IsUnique);
        Assert.Contains("lower", rich.KeyColumns![0].Expression);
        Assert.True(rich.KeyColumns[0].IsDescending);
        Assert.Equal("NOCASE", rich.KeyColumns[0].Collation);
        Assert.Equal("Other", rich.KeyColumns[1].Column);
        Assert.Contains("Other <> 'x,y'", rich.FilterDefinition);

        await provider.Ddl.AlterColumnAsync(context, "main", "RichIndexes", "Other",
            new ColumnDesign("Details", "TEXT"));
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "RichIndexes");
        rich = Assert.Single(definition.Indexes, index => index.Name == "IX_Rich_Expression");
        Assert.Equal("Details", rich.KeyColumns![1].Column);
        Assert.Contains("Details", rich.FilterDefinition);
        Assert.Equal("kept", (await provider.Data.GetPageAsync(
            context, "main", "RichIndexes", new TableDataRequest(1, 10))).Rows[0][2]);

        await provider.Ddl.CreateIndexAsync(context, "main", "RichIndexes", new IndexDesign(
            "IX_Rich_Created", [new IndexKeyDesign(null, Expression: "length(Details)", IsDescending: true)],
            FilterExpression: "Details IS NOT NULL"));
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "RichIndexes");
        Assert.Contains(definition.Indexes, index => index.Name == "IX_Rich_Created" &&
            index.KeyColumns![0].Expression == "length(Details)" && index.KeyColumns[0].IsDescending);
        await provider.Ddl.DropIndexAsync(context, "main", "RichIndexes", "IX_Rich_Created");
        Assert.DoesNotContain((await provider.Schema.GetTableDefinitionAsync(context, "main", "RichIndexes")).Indexes,
            index => index.Name == "IX_Rich_Created");
    }

    [Fact]
    public async Task Uses_table_list_not_keywords_to_detect_strict_and_without_rowid_options()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE KeywordNames (Id INTEGER PRIMARY KEY, STRICT TEXT, WITHOUT TEXT, ROWID TEXT);
            CREATE TABLE ActuallyStrict (Id INTEGER PRIMARY KEY, Value TEXT) STRICT;
            CREATE TABLE ActuallyCompact (Id TEXT PRIMARY KEY, Value TEXT) WITHOUT ROWID;
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "KeywordNames", "STRICT",
            new ColumnDesign("StrictValue", "TEXT"));
        await provider.Ddl.AlterColumnAsync(
            context, "main", "ActuallyStrict", "Value", new ColumnDesign("Text", "TEXT"));
        await provider.Ddl.AlterColumnAsync(
            context, "main", "ActuallyCompact", "Value", new ColumnDesign("Text", "TEXT"));

        // A column called STRICT does not make the table strict, and rebuilding a table that really
        // is strict keeps it that way.
        var keywordNames = await provider.Schema.GetTableDefinitionAsync(context, "main", "KeywordNames");
        var strict = await provider.Schema.GetTableDefinitionAsync(context, "main", "ActuallyStrict");
        var compact = await provider.Schema.GetTableDefinitionAsync(context, "main", "ActuallyCompact");
        Assert.Empty(keywordNames.TableOptions!);
        Assert.Equal(["STRICT"], strict.TableOptions);
        Assert.Equal(["WITHOUT ROWID"], compact.TableOptions);
    }

    [Fact]
    public async Task Keeps_unique_key_and_column_collations_through_a_rebuild()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE UniqueCollations (Id INTEGER PRIMARY KEY, Code TEXT);
            INSERT INTO UniqueCollations VALUES (1, 'same');
            CREATE TABLE ColumnCollations (Id INTEGER PRIMARY KEY, Code TEXT COLLATE NOCASE, Other TEXT);
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AddUniqueConstraintAsync(context, "main", "UniqueCollations",
            new UniqueConstraintDesign("UQ_UniqueCollations_Code",
                [new IndexKeyDesign("Code", Collation: "NOCASE")]));
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "UniqueCollations");
        Assert.Equal("NOCASE", definition.UniqueConstraints.Single().Columns.Single().Collation);
        await Assert.ThrowsAsync<GridletQueryException>(() => provider.Query.ExecuteAsync(context,
            "INSERT INTO UniqueCollations VALUES (2, 'SAME');", new QueryRequestOptions(10, 30)));

        await provider.Ddl.DropUniqueConstraintAsync(context, "main", "UniqueCollations",
            new ConstraintReference("UQ_UniqueCollations_Code"));
        await provider.Query.ExecuteAsync(context,
            "INSERT INTO UniqueCollations VALUES (2, 'SAME');", new QueryRequestOptions(10, 30));

        // Rebuilding a table with a column collation used to be refused; now the collation is kept.
        await provider.Ddl.AlterColumnAsync(
            context, "main", "ColumnCollations", "Other", new ColumnDesign("Details", "TEXT"));
        var rebuilt = await provider.Schema.GetTableDefinitionAsync(context, "main", "ColumnCollations");
        Assert.Equal("NOCASE", rebuilt.Columns.Single(column => column.Name == "Code").Collation);
        Assert.Contains(rebuilt.Columns, column => column.Name == "Details");
    }

    [Fact]
    public async Task Autoincrement_text_outside_the_primary_key_does_not_create_an_identity()
    {
        await provider.Query.ExecuteAsync(context,
            "CREATE TABLE AutoText (Id INTEGER PRIMARY KEY, Note TEXT DEFAULT ('AUTOINCREMENT'));",
            new QueryRequestOptions(10, 30));

        var before = await provider.Schema.GetTableDefinitionAsync(context, "main", "AutoText");
        Assert.False(before.Columns.Single(column => column.Name == "Id").IsIdentity);
        await provider.Writes.InsertRowAsync(context, "main", "AutoText",
            new Dictionary<string, object?> { ["Id"] = 42L, ["Note"] = "explicit" });

        await provider.Ddl.AlterColumnAsync(context, "main", "AutoText", "Note",
            new ColumnDesign("Description", "TEXT", DefaultExpression: "'AUTOINCREMENT'"));

        var after = await provider.Schema.GetTableDefinitionAsync(context, "main", "AutoText");
        Assert.False(after.Columns.Single(column => column.Name == "Id").IsIdentity);
        Assert.DoesNotContain("PRIMARY KEY AUTOINCREMENT",
            await provider.Schema.GetObjectDefinitionAsync(context, "main", "AutoText"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handles_sqlite_identifiers_containing_brackets_and_quotes()
    {
        const string table = "odd]\"name";
        await provider.Query.ExecuteAsync(context,
            "CREATE TABLE \"odd]\"\"name\" (\"Id\" INTEGER PRIMARY KEY, \"Value\" TEXT);",
            new QueryRequestOptions(10, 30));

        await provider.Writes.InsertRowAsync(context, "main", table,
            new Dictionary<string, object?> { ["Id"] = 1L, ["Value"] = "works" });
        var page = await provider.Data.GetPageAsync(
            context, "main", table, new TableDataRequest(1, 10));

        Assert.Equal("works", Assert.Single(page.Rows)[1]);
    }

    [Fact]
    public async Task Streaming_database_errors_are_translated_to_gridlet_query_errors()
    {
        await Assert.ThrowsAsync<GridletQueryException>(async () =>
        {
            await foreach (var _ in provider.Query.StreamAsync(context,
                "SELECT 1; SELECT * FROM __gridlet_missing_table__;",
                new QueryRequestOptions(10, 30)))
            {
            }
        });
    }

    [Fact]
    public async Task Rebuild_restores_an_initially_disabled_foreign_key_pragma()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridlet-foreign-keys-off-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            ForeignKeys = false,
        }.ToString();

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE DisabledForeignKeys (Id INTEGER PRIMARY KEY, Name TEXT);";
                await create.ExecuteNonQueryAsync();
            }

            Assert.Equal(0L, await ReadForeignKeyPragmaAsync(connection));
            var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(
                connection, "DisabledForeignKeys", CancellationToken.None);
            await SqliteTableDdlService.RebuildTableAsync(
                connection,
                definition,
                [
                    new ColumnDesign("Id", "INTEGER", IsNullable: false, IsPrimaryKey: true),
                    new ColumnDesign("Description", "TEXT"),
                ],
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Description"] = "Name",
                },
                CancellationToken.None);

            Assert.Equal(0L, await ReadForeignKeyPragmaAsync(connection));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Rebuild_ignores_unrelated_preexisting_foreign_key_violations()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE OrphanParents (Id INTEGER PRIMARY KEY);
            CREATE TABLE OrphanChildren (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL REFERENCES OrphanParents(Id)
            );
            CREATE TABLE UnrelatedRebuild (Id INTEGER PRIMARY KEY, Name TEXT);
            INSERT INTO UnrelatedRebuild VALUES (1, 'before');
            PRAGMA foreign_keys = OFF;
            INSERT INTO OrphanChildren VALUES (1, 999);
            PRAGMA foreign_keys = ON;
            """,
            new QueryRequestOptions(10, 30));

        await provider.Ddl.AlterColumnAsync(context, "main", "UnrelatedRebuild", "Name",
            new ColumnDesign("Description", "TEXT"));

        var result = await provider.Query.ExecuteAsync(context,
            "PRAGMA foreign_key_check;", new QueryRequestOptions(10, 30));
        var violation = Assert.Single(Assert.Single(result.ResultSets).Rows);
        Assert.Equal("OrphanChildren", violation[0]);
        Assert.Equal("OrphanParents", violation[2]);
    }

    private static async Task<long> ReadForeignKeyPragmaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
