using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// Covers how a SQLite row is addressed for editing when the table has no primary key Gridlet can
/// rely on, which is what makes such tables editable at all.
/// </summary>
public sealed class SqliteRowIdentityTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-rowid-{Guid.NewGuid():N}.db");
    private readonly SqliteSchemaReader schema = new();
    private readonly SqliteTableDataService data = new();
    private readonly SqliteTableWriteService writes = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "RowIdentity",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Notes (Body TEXT NOT NULL, Author TEXT);
            INSERT INTO Notes VALUES ('first', 'ada'), ('first', 'ada'), ('second', 'grace');
            CREATE TABLE Counters (Id INTEGER PRIMARY KEY, Total INTEGER NOT NULL);
            INSERT INTO Counters VALUES (1, 10);
            CREATE TABLE Codes (Code TEXT PRIMARY KEY, Label TEXT);
            INSERT INTO Codes VALUES ('a', 'alpha'), (NULL, 'nameless'), (NULL, 'anonymous');
            CREATE TABLE StrictCodes (Code TEXT PRIMARY KEY NOT NULL, Label TEXT);
            INSERT INTO StrictCodes VALUES ('a', 'alpha');
            CREATE TABLE CompactCodes (Code TEXT PRIMARY KEY, Label TEXT) WITHOUT ROWID;
            INSERT INTO CompactCodes VALUES ('a', 'alpha');
            CREATE TABLE Shadowed (rowid TEXT, Body TEXT);
            INSERT INTO Shadowed VALUES ('not the rowid', 'first');
            CREATE TABLE FullyShadowed (rowid TEXT, _rowid_ TEXT, oid TEXT, Code TEXT PRIMARY KEY);
            INSERT INTO FullyShadowed VALUES ('a', 'b', 'c', NULL), ('d', 'e', 'f', NULL);
            CREATE VIEW NoteBodies AS SELECT Body FROM Notes;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("Notes", RowIdentityKinds.RowId, "rowid")]
    [InlineData("Counters", RowIdentityKinds.PrimaryKey, "Id")]
    [InlineData("StrictCodes", RowIdentityKinds.PrimaryKey, "Code")]
    [InlineData("CompactCodes", RowIdentityKinds.PrimaryKey, "Code")]
    [InlineData("Shadowed", RowIdentityKinds.RowId, "_rowid_")]
    public async Task Tables_report_how_one_row_is_addressed(string table, string kind, string column)
    {
        var definition = await schema.GetTableDefinitionAsync(context, "main", table);

        Assert.NotNull(definition.RowIdentity);
        Assert.Equal(kind, definition.RowIdentity.Kind);
        Assert.Equal([column], definition.RowIdentity.Columns);
    }

    [Fact]
    public async Task A_primary_key_sqlite_allows_to_be_null_falls_back_to_the_rowid()
    {
        var definition = await schema.GetTableDefinitionAsync(context, "main", "Codes");

        Assert.NotNull(definition.RowIdentity);
        Assert.Equal(RowIdentityKinds.RowId, definition.RowIdentity.Kind);
    }

    /// <summary>
    /// A primary key SQLite lets hold NULLs cannot address one row, and here every rowid alias is
    /// taken by a real column, so there is nothing left to address a row with. Offering the key
    /// anyway would let an edit meant for one row change every row that shares its NULL.
    /// </summary>
    [Fact]
    public async Task A_table_with_no_usable_key_and_no_rowid_left_is_read_only()
    {
        var definition = await schema.GetTableDefinitionAsync(context, "main", "FullyShadowed");
        var page = await data.GetPageAsync(context, "main", "FullyShadowed", new TableDataRequest(1, 10));

        Assert.Null(definition.RowIdentity);
        Assert.Null(page.RowIdentity);
        Assert.Null(page.RowKeys);
    }

    [Fact]
    public async Task Views_report_no_row_identity()
    {
        var definition = await schema.GetTableDefinitionAsync(context, "main", "NoteBodies");

        Assert.Null(definition.RowIdentity);
    }

    [Fact]
    public async Task A_data_page_carries_the_key_of_every_row_without_showing_it()
    {
        var page = await data.GetPageAsync(context, "main", "Notes", new TableDataRequest(1, 10));

        Assert.Equal(["Body", "Author"], page.Columns.Select(column => column.Name).ToArray());
        Assert.Equal(2, page.Rows[0].Length);
        Assert.NotNull(page.RowIdentity);
        Assert.Equal(RowIdentityKinds.RowId, page.RowIdentity.Kind);
        Assert.NotNull(page.RowKeys);
        Assert.Equal([[1L], [2L], [3L]], page.RowKeys);
    }

    [Fact]
    public async Task A_data_page_repeats_primary_key_values_as_row_keys()
    {
        var page = await data.GetPageAsync(context, "main", "Counters", new TableDataRequest(1, 10));

        Assert.NotNull(page.RowIdentity);
        Assert.Equal(RowIdentityKinds.PrimaryKey, page.RowIdentity.Kind);
        Assert.Equal(["Id"], page.RowIdentity.Columns);
        Assert.Equal([[1L]], page.RowKeys);
    }

    [Fact]
    public async Task A_view_page_carries_no_row_keys()
    {
        var page = await data.GetPageAsync(context, "main", "NoteBodies", new TableDataRequest(1, 10));

        Assert.Null(page.RowIdentity);
        Assert.Null(page.RowKeys);
    }

    [Fact]
    public async Task A_row_of_a_table_without_a_primary_key_can_be_updated_and_deleted_by_rowid()
    {
        var page = await data.GetPageAsync(context, "main", "Notes", new TableDataRequest(1, 10));
        var firstKey = new Dictionary<string, object?> { ["rowid"] = page.RowKeys![0][0] };

        var updated = await writes.UpdateRowAsync(context, "main", "Notes", firstKey,
            new Dictionary<string, object?> { ["Author"] = "ada lovelace" });
        var deleted = await writes.DeleteRowAsync(context, "main", "Notes",
            new Dictionary<string, object?> { ["rowid"] = page.RowKeys[1][0] });

        // Rows 1 and 2 are identical apart from their rowid, so anything but a rowid key would
        // have touched both.
        Assert.Equal(1, updated);
        Assert.Equal(1, deleted);
        var after = await data.GetPageAsync(context, "main", "Notes", new TableDataRequest(1, 10));
        Assert.Equal(
            [["first", "ada lovelace"], ["second", "grace"]],
            after.Rows);
    }

    [Fact]
    public async Task A_shadowed_rowid_column_is_written_as_data_not_as_the_key()
    {
        var page = await data.GetPageAsync(context, "main", "Shadowed", new TableDataRequest(1, 10));

        var updated = await writes.UpdateRowAsync(context, "main", "Shadowed",
            new Dictionary<string, object?> { ["_rowid_"] = page.RowKeys![0][0] },
            new Dictionary<string, object?> { ["rowid"] = "still not the rowid" });

        Assert.Equal(1, updated);
        var after = await data.GetPageAsync(context, "main", "Shadowed", new TableDataRequest(1, 10));
        Assert.Equal("still not the rowid", after.Rows[0][0]);
    }

    [Fact]
    public async Task Rowid_is_only_accepted_as_a_key_where_it_is_the_row_identity()
    {
        var exception = await Assert.ThrowsAsync<GridletValidationException>(() =>
            writes.DeleteRowAsync(context, "main", "Counters",
                new Dictionary<string, object?> { ["rowid"] = 1L }));

        Assert.Contains("rowid", exception.Message, StringComparison.Ordinal);
    }
}
