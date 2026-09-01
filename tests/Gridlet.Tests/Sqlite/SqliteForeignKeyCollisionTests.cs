using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// SQLite does not require constraint names to be unique within a table. A repeated name is still
/// what the database holds, so it is reported and preserved; it is the drop path that has to refuse
/// to guess which constraint a repeated name means.
/// </summary>
public sealed class SqliteForeignKeyCollisionTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-fkdup-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public Task InitializeAsync()
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
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    private Task RunAsync(string sql)
        => provider.Query.ExecuteAsync(context, sql, new QueryRequestOptions(100, 30));

    private async Task<string> ReadCreateSqlAsync(string table)
    {
        var result = await provider.Query.ExecuteAsync(context,
            "SELECT sql FROM sqlite_schema WHERE name = '" + table.Replace("'", "''") + "';",
            new QueryRequestOptions(10, 30));
        return (string)result.ResultSets[0].Rows[0][0]!;
    }

    [Fact]
    public async Task Reports_both_keys_when_two_share_a_declared_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER,
                B INTEGER,
                CONSTRAINT fk_dup FOREIGN KEY (A) REFERENCES Parent (Id),
                CONSTRAINT fk_dup FOREIGN KEY (B) REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal(2, definition.ForeignKeys.Count);
        Assert.All(definition.ForeignKeys, key =>
        {
            Assert.Equal("fk_dup", key.Name);
            Assert.False(key.IsNameSynthesized);
        });
    }

    /// <summary>
    /// A rebuild that has nothing to do with the repeated name still has to write both names back.
    /// </summary>
    [Fact]
    public async Task Keeps_both_declared_names_across_an_unrelated_rebuild()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER,
                B INTEGER,
                Note TEXT,
                CONSTRAINT fk_dup FOREIGN KEY (A) REFERENCES Parent (Id),
                CONSTRAINT fk_dup FOREIGN KEY (B) REFERENCES Parent (Id)
            );
            """);

        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
            new ColumnDesign("Note", "VARCHAR(200)"));

        var createSql = await ReadCreateSqlAsync("Child");
        Assert.Equal(2, definitionNameCount(createSql, "fk_dup"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal(2, definition.ForeignKeys.Count);
        Assert.All(definition.ForeignKeys, key => Assert.False(key.IsNameSynthesized));

        static int definitionNameCount(string sql, string name)
        {
            var count = 0;
            for (var index = sql.IndexOf(name, StringComparison.Ordinal); index >= 0;
                 index = sql.IndexOf(name, index + name.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }

    [Fact]
    public async Task Refuses_to_drop_a_name_that_matches_two_foreign_keys()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER,
                B INTEGER,
                CONSTRAINT fk_dup FOREIGN KEY (A) REFERENCES Parent (Id),
                CONSTRAINT fk_dup FOREIGN KEY (B) REFERENCES Parent (Id)
            );
            """);

        var error = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.DropConstraintAsync(context, "main", "Child", "fk_dup"));
        Assert.Contains("more than one constraint", error.Message, StringComparison.Ordinal);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal(2, definition.ForeignKeys.Count);
    }

    /// <summary>
    /// The primary key is resolved before foreign keys, so a foreign key sharing its name would
    /// otherwise be dropped by removing the primary key.
    /// </summary>
    [Fact]
    public async Task Refuses_to_drop_a_name_shared_with_the_primary_key()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER NOT NULL,
                ParentId INTEGER,
                CONSTRAINT PK_Child PRIMARY KEY (Id),
                CONSTRAINT PK_Child FOREIGN KEY (ParentId) REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var primaryKey = definition.Indexes.Single(index => index.IsPrimaryKey);
        Assert.Equal("PK_Child", primaryKey.Name);
        Assert.Equal("PK_Child", Assert.Single(definition.ForeignKeys).Name);

        await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.DropConstraintAsync(context, "main", "Child", "PK_Child"));

        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Contains(definition.Indexes, index => index.IsPrimaryKey);
        Assert.Single(definition.ForeignKeys);
    }

    /// <summary>
    /// A name that matches one key is not ambiguous, whatever else the table holds.
    /// </summary>
    [Fact]
    public async Task Drops_one_key_when_the_name_matches_only_it()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER,
                B INTEGER,
                CONSTRAINT fk_a FOREIGN KEY (A) REFERENCES Parent (Id),
                CONSTRAINT fk_b FOREIGN KEY (B) REFERENCES Parent (Id)
            );
            """);

        await provider.Ddl.DropConstraintAsync(context, "main", "Child", "fk_a");

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var remaining = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_b", remaining.Name);
        Assert.False(remaining.IsNameSynthesized);
    }

    /// <summary>
    /// The label for an unnamed key is Gridlet's own choice, so it must not land on a name the table
    /// already declares - that collision would be Gridlet's doing, and would block both keys.
    /// </summary>
    [Fact]
    public async Task Chooses_a_label_that_avoids_a_declared_name()
    {
        // The unnamed key on B takes pragma id 0, whose usual label is FK_Child_0 - the name the
        // other key declares.
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER,
                B INTEGER,
                CONSTRAINT FK_Child_0 FOREIGN KEY (A) REFERENCES Parent (Id),
                FOREIGN KEY (B) REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var declared = definition.ForeignKeys.Single(key => key.Columns[0].Column == "A");
        var unnamed = definition.ForeignKeys.Single(key => key.Columns[0].Column == "B");
        Assert.Equal("FK_Child_0", declared.Name);
        Assert.False(declared.IsNameSynthesized);
        Assert.True(unnamed.IsNameSynthesized);
        Assert.NotEqual(declared.Name, unnamed.Name, StringComparer.OrdinalIgnoreCase);

        // Neither key is blocked: each name still identifies exactly one of them.
        await provider.Ddl.DropConstraintAsync(context, "main", "Child", unnamed.Name);
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal("A", Assert.Single(definition.ForeignKeys).Columns[0].Column);
    }

    /// <summary>
    /// The drop route resolves the primary key through the same name, so a label that landed on the
    /// primary key's name would make neither constraint droppable.
    /// </summary>
    [Fact]
    public async Task Chooses_a_label_that_avoids_the_primary_key_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER NOT NULL,
                ParentId INTEGER REFERENCES Parent (Id),
                CONSTRAINT FK_Child_0 PRIMARY KEY (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var primaryKey = definition.Indexes.Single(index => index.IsPrimaryKey);
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.True(foreignKey.IsNameSynthesized);
        Assert.NotEqual(primaryKey.Name, foreignKey.Name, StringComparer.OrdinalIgnoreCase);

        await provider.Ddl.DropConstraintAsync(context, "main", "Child", foreignKey.Name);
        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Empty(definition.ForeignKeys);
        Assert.Contains(definition.Indexes, index => index.IsPrimaryKey);
    }

    /// <summary>
    /// SQLite accepts a blank constraint name; Gridlet cannot write an empty identifier back. The
    /// designer refuses the rebuild rather than dropping the name on the way through.
    /// </summary>
    [Fact]
    public async Task Refuses_to_rebuild_a_table_with_a_blank_constraint_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER,
                Note TEXT,
                CONSTRAINT "" FOREIGN KEY (ParentId) REFERENCES Parent (Id)
            );
            """);

        var error = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
                new ColumnDesign("Note", "VARCHAR(200)")));
        Assert.Contains("blank foreign-key constraint names", error.Message, StringComparison.Ordinal);

        Assert.Contains("CONSTRAINT \"\"", await ReadCreateSqlAsync("Child"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two unnamed keys are told apart by their labels, which are unique because the pragma numbers
    /// them.
    /// </summary>
    [Fact]
    public async Task Drops_one_of_two_unnamed_keys()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER REFERENCES Parent (Id),
                B INTEGER REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal(2, definition.ForeignKeys.Count);
        Assert.All(definition.ForeignKeys, key => Assert.True(key.IsNameSynthesized));
        var target = definition.ForeignKeys.Single(key => key.Columns[0].Column == "A");

        await provider.Ddl.DropConstraintAsync(context, "main", "Child", target.Name);

        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal("B", Assert.Single(definition.ForeignKeys).Columns[0].Column);
    }
}
