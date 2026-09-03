using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteRenameAndTruncateTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-rename-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Rename",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Region TEXT);
            CREATE UNIQUE INDEX UX_Customers_Name ON Customers (Name DESC) WHERE Region IS NOT NULL;
            INSERT INTO Customers (Name, Region) VALUES ('Ada', 'EU'), ('Grace', 'US');
            CREATE VIEW CustomerNames AS SELECT Name FROM Customers;
            """,
            new QueryRequestOptions(100, 30));
    }

    public Task DisposeAsync()
    {
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_table_is_renamed_and_keeps_its_rows()
    {
        await provider.Ddl.RenameObjectAsync(context, "main", "Customers", DbObjectType.Table, "Clients");

        var objects = await provider.Schema.GetObjectsAsync(context);
        Assert.Contains(objects, o => o.Name == "Clients");
        Assert.DoesNotContain(objects, o => o.Name == "Customers");
        var page = await provider.Data.GetPageAsync(context, "main", "Clients", new TableDataRequest(1, 10));
        Assert.Equal(2, page.TotalRows);
    }

    /// <summary>
    /// SQLite has no ALTER INDEX, so the index is recreated. What matters is that everything about
    /// it survives: uniqueness, key order and direction, and the partial-index filter.
    /// </summary>
    [Fact]
    public async Task An_index_is_renamed_without_losing_what_it_is()
    {
        await provider.Ddl.RenameIndexAsync(
            context, "main", "Customers", "UX_Customers_Name", "UX_Clients_Name");

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        var index = Assert.Single(definition.Indexes, i => !i.IsPrimaryKey);
        Assert.Equal("UX_Clients_Name", index.Name);
        Assert.True(index.IsUnique);
        Assert.Equal("Name", Assert.Single(index.KeyColumns!).Column);
        Assert.True(Assert.Single(index.KeyColumns!).IsDescending);
        Assert.Contains("Region", index.FilterDefinition!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Renaming an index means dropping and recreating it, and a virtual table's shadow tables are
    /// the module's own storage: rewriting anything on them is how a full-text index stops working.
    /// The other operations on internal tables are already refused; this one has to be too.
    /// </summary>
    [Fact]
    public async Task An_index_on_an_internal_table_cannot_be_renamed()
    {
        await provider.Query.ExecuteAsync(context,
            """
            CREATE VIRTUAL TABLE SearchDocs USING fts5(Body);
            INSERT INTO SearchDocs (Body) VALUES ('keep me');
            CREATE INDEX IX_SearchDocs_Data ON SearchDocs_data (id);
            """,
            new QueryRequestOptions(10, 30));

        var exception = await Assert.ThrowsAsync<GridletValidationException>(() => provider.Ddl.RenameIndexAsync(
            context, "main", "SearchDocs_data", "IX_SearchDocs_Data", "IX_Renamed"));

        Assert.Contains("internal", exception.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing was dropped on the way to refusing, so the table still works.
        var result = await provider.Query.ExecuteAsync(context,
            "SELECT Body FROM SearchDocs WHERE SearchDocs MATCH 'keep';", new QueryRequestOptions(10, 30));
        Assert.Equal("keep me", Assert.Single(Assert.Single(result.ResultSets).Rows)[0]);
    }

    [Fact]
    public async Task A_view_says_what_to_do_instead_of_being_renamed_badly()
    {
        var exception = await Assert.ThrowsAsync<GridletValidationException>(
            () => provider.Ddl.RenameObjectAsync(context, "main", "CustomerNames", DbObjectType.View, "Names"));

        Assert.Contains("definition", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rename changes the name; it does not move the object. Quoting would accept a dotted name
    /// and leave a table whose name only looks qualified, so it is refused - the same rule the SQL
    /// Server provider applies.
    /// </summary>
    [Theory]
    [InlineData("other.Clients")]
    [InlineData("main.Clients")]
    public async Task A_qualified_new_name_is_refused(string newName)
    {
        var table = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.RenameObjectAsync(context, "main", "Customers", DbObjectType.Table, newName));
        var index = await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.RenameIndexAsync(context, "main", "Customers", "UX_Customers_Name", newName));

        Assert.Contains("without a schema", table.Message, StringComparison.Ordinal);
        Assert.Contains("without a schema", index.Message, StringComparison.Ordinal);

        // The refusal happens before anything is touched.
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        Assert.Contains(definition.Indexes, i => i.Name == "UX_Customers_Name");
    }

    /// <summary>
    /// Surrounding whitespace is almost always a paste artefact, and quoting it would create an
    /// object nobody can name again without reproducing the spaces exactly.
    /// </summary>
    [Fact]
    public async Task Surrounding_whitespace_is_trimmed_rather_than_becoming_part_of_the_name()
    {
        await provider.Ddl.RenameObjectAsync(context, "main", "Customers", DbObjectType.Table, "  Clients  ");
        await provider.Ddl.RenameIndexAsync(
            context, "main", "Clients", "UX_Customers_Name", " UX_Clients_Name ");

        var objects = await provider.Schema.GetObjectsAsync(context);
        Assert.Contains(objects, o => o.Name == "Clients");
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Clients");
        Assert.Equal("UX_Clients_Name", Assert.Single(definition.Indexes, i => !i.IsPrimaryKey).Name);
    }

    [Fact]
    public async Task An_empty_new_name_is_refused()
        => await Assert.ThrowsAsync<GridletValidationException>(() =>
            provider.Ddl.RenameObjectAsync(context, "main", "Customers", DbObjectType.Table, "  "));

    [Fact]
    public async Task Emptying_a_table_keeps_the_table_and_its_indexes()
    {
        await provider.Ddl.TruncateTableAsync(context, "main", "Customers");

        var page = await provider.Data.GetPageAsync(context, "main", "Customers", new TableDataRequest(1, 10));
        Assert.Equal(0, page.TotalRows);
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        Assert.Equal(3, definition.Columns.Count);
        Assert.Contains(definition.Indexes, index => index.Name == "UX_Customers_Name");
    }

    [Fact]
    public async Task A_view_cannot_be_emptied()
        => await Assert.ThrowsAsync<GridletValidationException>(
            () => provider.Ddl.TruncateTableAsync(context, "main", "CustomerNames"));
}
