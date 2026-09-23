using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// A SQLite foreign key declared <c>DEFERRABLE INITIALLY DEFERRED</c> is checked at commit rather
/// than after each statement. <c>pragma_foreign_key_list</c> does not report that, so these tests
/// cover reading it from the CREATE statement and keeping it through a designer rebuild.
/// </summary>
public sealed class SqliteDeferredForeignKeyTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-fk-deferred-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private string connectionString = null!;
    private GridletConnectionContext context = null!;

    public Task InitializeAsync()
    {
        connectionString = new SqliteConnectionStringBuilder
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

    [Theory]
    [InlineData("CONSTRAINT fk FOREIGN KEY (ParentId) REFERENCES Parent (Id) DEFERRABLE INITIALLY DEFERRED", true)]
    [InlineData("FOREIGN KEY (ParentId) REFERENCES Parent (Id) ON DELETE SET NULL deferrable initially deferred", true)]
    [InlineData("FOREIGN KEY (ParentId) REFERENCES Parent (Id) DEFERRABLE", false)]
    [InlineData("FOREIGN KEY (ParentId) REFERENCES Parent (Id) DEFERRABLE INITIALLY IMMEDIATE", false)]
    [InlineData("FOREIGN KEY (ParentId) REFERENCES Parent (Id) NOT DEFERRABLE INITIALLY DEFERRED", false)]
    [InlineData("FOREIGN KEY (ParentId) REFERENCES Parent (Id)", false)]
    public async Task Reads_whether_a_table_level_key_is_deferred(string constraint, bool deferred)
    {
        await RunAsync($"""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER, {constraint});
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.Equal(deferred, Assert.Single(definition.ForeignKeys).IsDeferred);
    }

    [Fact]
    public async Task Reads_a_deferred_column_level_key_and_stops_at_the_next_constraint()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                Deferred INTEGER REFERENCES Parent (Id) ON DELETE SET DEFAULT DEFERRABLE INITIALLY DEFERRED NOT NULL DEFAULT 0,
                Immediate INTEGER REFERENCES Parent (Id) NOT NULL CHECK (Immediate > 0)
            );
            """);

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var byColumn = definition.ForeignKeys.ToDictionary(key => key.Columns[0].Column);
        Assert.True(byColumn["Deferred"].IsDeferred);
        Assert.False(byColumn["Immediate"].IsDeferred);
    }

    [Fact]
    public async Task Keeps_a_deferred_key_deferred_across_a_designer_rebuild()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL,
                Note TEXT,
                CONSTRAINT fk_child_parent FOREIGN KEY (ParentId) REFERENCES Parent (Id) DEFERRABLE INITIALLY DEFERRED
            );
            """);

        // Altering a column replays the whole table, which is where the clause used to be lost.
        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
            new ColumnDesign("Note", "VARCHAR(200)"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        var foreignKey = Assert.Single(definition.ForeignKeys);
        Assert.True(foreignKey.IsDeferred);
        Assert.Equal("fk_child_parent", foreignKey.Name);
        await AssertCheckedAtCommitAsync();
    }

    [Fact]
    public async Task Rebuilds_a_table_whose_key_says_not_deferrable_initially_deferred()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parent (Id) NOT DEFERRABLE INITIALLY DEFERRED,
                Note TEXT
            );
            """);

        // The clause reads like a deferred key but is checked immediately, so the rebuild can
        // write the key back without it rather than refusing the table.
        await provider.Ddl.AlterColumnAsync(context, "main", "Child", "Note",
            new ColumnDesign("Note", "VARCHAR(200)"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.False(Assert.Single(definition.ForeignKeys).IsDeferred);
    }

    [Fact]
    public async Task Adds_a_deferred_key_through_the_designer()
    {
        await RunAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL);
            """);

        await provider.Ddl.AddForeignKeyAsync(context, "main", "Child",
            new ForeignKeyDesign("FK_Child_Parent", "main", "Parent",
                [new ForeignKeyColumnPair("ParentId", "Id")], IsDeferred: true));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Child");
        Assert.True(Assert.Single(definition.ForeignKeys).IsDeferred);
        await AssertCheckedAtCommitAsync();
    }

    /// <summary>
    /// A child row inserted before its parent is accepted only when the key waits for the commit.
    /// </summary>
    private async Task AssertCheckedAtCommitAsync()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Child (Id, ParentId) VALUES (1, 42); INSERT INTO Parent (Id) VALUES (42);";
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
