using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>Filtering runs in SQLite, so these tests use a real database and real affinity rules.</summary>
public sealed class SqliteTableDataFilterTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-filter-{Guid.NewGuid():N}.db");
    private readonly SqliteTableDataService data = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Filter",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Price NUMERIC,
                Notes TEXT
            );
            INSERT INTO Products (Name, Price, Notes) VALUES
                ('Widget', 5, 'in stock'),
                ('Wide widget', 50, NULL),
                ('Gadget', 12.5, '50% off'),
                ('Gizmo', 100, 'discontinued');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    private async Task<string[]> NamesAsync(params TableDataFilter[] filters)
    {
        var page = await data.GetPageAsync(
            context, "main", "Products", new TableDataRequest(1, 50, "Id", SortDirection.Ascending, filters));
        return page.Rows.Select(row => (string)row[1]!).ToArray();
    }

    [Fact]
    public async Task Text_conditions_match_across_the_whole_table()
    {
        Assert.Equal(["Widget", "Wide widget"], await NamesAsync(
            new TableDataFilter("Name", FilterOperator.Contains, "wid")));
        Assert.Equal(["Widget", "Wide widget"], await NamesAsync(
            new TableDataFilter("Name", FilterOperator.StartsWith, "wi")));
        Assert.Equal(["Widget", "Wide widget"], await NamesAsync(
            new TableDataFilter("Name", FilterOperator.EndsWith, "et"),
            new TableDataFilter("Name", FilterOperator.NotContains, "gad")));
    }

    /// <summary>
    /// SQLite compares a text '50' against a numeric column as unequal, so the value has to be bound
    /// as a number. Without that this filter would silently return nothing.
    /// </summary>
    [Fact]
    public async Task A_numeric_column_is_compared_as_a_number()
    {
        Assert.Equal(["Wide widget"], await NamesAsync(
            new TableDataFilter("Price", FilterOperator.Equals, "50")));
        Assert.Equal(["Wide widget", "Gizmo"], await NamesAsync(
            new TableDataFilter("Price", FilterOperator.GreaterThanOrEqual, "50")));
        Assert.Equal(["Widget", "Gadget"], await NamesAsync(
            new TableDataFilter("Price", FilterOperator.LessThan, "12.6")));
    }

    [Fact]
    public async Task A_wildcard_in_the_value_matches_itself()
        => Assert.Equal(["Gadget"], await NamesAsync(
            new TableDataFilter("Notes", FilterOperator.Contains, "50%")));

    [Fact]
    public async Task Null_checks_find_rows_with_and_without_a_value()
    {
        Assert.Equal(["Wide widget"], await NamesAsync(
            new TableDataFilter("Notes", FilterOperator.IsNull)));
        Assert.Equal(["Widget", "Gadget", "Gizmo"], await NamesAsync(
            new TableDataFilter("Notes", FilterOperator.IsNotNull)));
    }

    [Fact]
    public async Task The_total_counts_only_the_matching_rows()
    {
        var page = await data.GetPageAsync(context, "main", "Products",
            new TableDataRequest(1, 1, null, SortDirection.Ascending,
                [new TableDataFilter("Name", FilterOperator.Contains, "wid")]));

        Assert.Single(page.Rows);
        Assert.Equal(2, page.TotalRows);
    }

    [Fact]
    public async Task An_unknown_filter_column_is_rejected()
        => await Assert.ThrowsAsync<GridletValidationException>(() => data.GetPageAsync(
            context, "main", "Products",
            new TableDataRequest(1, 50, null, SortDirection.Ascending,
                [new TableDataFilter("Name\"; DROP TABLE Products --", FilterOperator.Equals, "x")])));
}
