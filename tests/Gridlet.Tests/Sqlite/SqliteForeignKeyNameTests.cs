using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// SQLite reports foreign keys through <c>pragma_foreign_key_list</c>, which does not return the
/// declared CONSTRAINT name. These tests cover recovering that name from the CREATE statement and
/// carrying it, or the absence of one, through a designer rebuild.
/// </summary>
public sealed class SqliteForeignKeyNameTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-fk-{Guid.NewGuid():N}.db");
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
            "SELECT sql FROM sqlite_schema WHERE name = " + SqliteLiteral(table) + ";",
            new QueryRequestOptions(10, 30));
        return (string)result.ResultSets[0].Rows[0][0]!;
    }

    private static string SqliteLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    [Fact]
    public async Task Reads_the_declared_constraint_name_instead_of_inventing_one()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL,
                CONSTRAINT fk_child_parent FOREIGN KEY (ParentId) REFERENCES Parent (Id) ON DELETE CASCADE
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_child_parent", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
    }

    [Fact]
    public async Task Reads_a_column_level_constraint_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER CONSTRAINT fk_inline REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_inline", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
    }

    [Fact]
    public async Task Marks_a_key_declared_without_a_name_as_synthesized()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.True(foreignKey.IsNameSynthesized);
        Assert.Equal("FK_Child_0", foreignKey.Name);
    }

    /// <summary>
    /// pragma_foreign_key_list numbers keys in reverse declaration order, so a table with several
    /// keys is the case a naive positional pairing gets wrong.
    /// </summary>
    [Fact]
    public async Task Matches_names_to_the_right_key_when_several_are_declared()
    {
        await RunAsync("""
            CREATE TABLE P1 (Id INTEGER PRIMARY KEY);
            CREATE TABLE P2 (Id INTEGER PRIMARY KEY);
            CREATE TABLE P3 (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                A INTEGER CONSTRAINT fk_a REFERENCES P1 (Id),
                B INTEGER,
                C INTEGER,
                CONSTRAINT fk_b FOREIGN KEY (B) REFERENCES P2 (Id),
                FOREIGN KEY (C) REFERENCES P3 (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var byColumn = definition.ForeignKeys.ToDictionary(
            fk => fk.Columns[0].Column, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("P1", byColumn["A"].ReferencedTable);
        Assert.Equal("fk_a", byColumn["A"].Name);
        Assert.False(byColumn["A"].IsNameSynthesized);

        Assert.Equal("P2", byColumn["B"].ReferencedTable);
        Assert.Equal("fk_b", byColumn["B"].Name);
        Assert.False(byColumn["B"].IsNameSynthesized);

        Assert.Equal("P3", byColumn["C"].ReferencedTable);
        Assert.True(byColumn["C"].IsNameSynthesized);
    }

    [Fact]
    public async Task Reads_a_composite_key_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (A INTEGER, B INTEGER, PRIMARY KEY (A, B));
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                "Parent A" INTEGER,
                ParentB INTEGER,
                CONSTRAINT "fk composite" FOREIGN KEY ("Parent A", ParentB) REFERENCES Parent (A, B)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk composite", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
        Assert.Equal(
            [new ForeignKeyColumnPair("Parent A", "A"), new ForeignKeyColumnPair("ParentB", "B")],
            foreignKey.Columns);
    }

    /// <summary>
    /// A comment inside the key's column list is trivia, not part of a column name, so the name is
    /// still recoverable.
    /// </summary>
    [Fact]
    public async Task Reads_a_name_through_comments_in_the_column_list()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER,
                CONSTRAINT fk_commented FOREIGN KEY (ParentId /* owning parent */) REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_commented", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
    }

    [Fact]
    public async Task Reads_a_name_from_a_single_quoted_declaration()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER,
                CONSTRAINT fk_quoted FOREIGN KEY ('ParentId') REFERENCES 'Parent' (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_quoted", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
    }

    [Fact]
    public async Task Keeps_the_declared_name_across_a_designer_rebuild()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL,
                Note TEXT,
                CONSTRAINT fk_child_parent FOREIGN KEY (ParentId) REFERENCES Parent (Id) ON DELETE CASCADE
            );
            """);

        // Altering a column is the path that replays the whole table, which is where a foreign-key
        // name is lost if it was never read.
        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
            new ColumnDesign("Note", "VARCHAR(200)"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_child_parent", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
        Assert.Equal("CASCADE", foreignKey.OnDelete);
        Assert.Contains("fk_child_parent", await ReadCreateSqlAsync("Child"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_the_declared_name_when_the_referencing_column_is_renamed()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL,
                CONSTRAINT fk_child_parent FOREIGN KEY (ParentId) REFERENCES Parent (Id)
            );
            """);

        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "ParentId",
            new ColumnDesign("OwnerId", "INTEGER", IsNullable: false));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("fk_child_parent", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
        Assert.Equal(new ForeignKeyColumnPair("OwnerId", "Id"), Assert.Single(foreignKey.Columns));
    }

    [Fact]
    public async Task Does_not_name_an_unnamed_key_during_a_designer_rebuild()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parent (Id),
                Note TEXT
            );
            """);

        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
            new ColumnDesign("Note", "VARCHAR(200)"));

        var createSql = await ReadCreateSqlAsync("Child");
        Assert.Contains("FOREIGN KEY", createSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)CONSTRAINT\s+\S+\s+FOREIGN\s+KEY", createSql);
        Assert.DoesNotContain("FK_Child", createSql, StringComparison.OrdinalIgnoreCase);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.True(foreignKey.IsNameSynthesized);
        Assert.Equal("Parent", foreignKey.ReferencedTable);
        Assert.Equal(new ForeignKeyColumnPair("ParentId", "Id"), Assert.Single(foreignKey.Columns));
    }

    [Fact]
    public async Task Keeps_a_name_added_through_the_designer()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER);
            """);

        await provider.Ddl.AddForeignKeyAsync(context, "main", "Child",
            new ForeignKeyDesign("FK_Child_Parent", "main", "Parent",
                [new ForeignKeyColumnPair("ParentId", "Id")]));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.Equal("FK_Child_Parent", foreignKey.Name);
        Assert.False(foreignKey.IsNameSynthesized);
    }

    /// <summary>
    /// The synthesized-name marker describes a key already in the database. A request that set it
    /// would report a name the schema does not hold, so it is refused.
    /// </summary>
    [Fact]
    public async Task Refuses_a_request_that_asks_for_a_synthesized_name()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER);
            """);

        await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.AddForeignKeyAsync(context, "main", "Child",
                new ForeignKeyDesign("FK_Child_Parent", "main", "Parent",
                    [new ForeignKeyColumnPair("ParentId", "Id")],
                    "NO ACTION", "NO ACTION", IsNameSynthesized: true)));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Empty(definition.ForeignKeys);
    }

    [Fact]
    public async Task Drops_an_unnamed_key_by_its_synthesized_label()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parent (Id)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        await provider.Ddl.DropConstraintAsync(context, "main", "Child", foreignKey.Name);

        definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Empty(definition.ForeignKeys);
    }
}
