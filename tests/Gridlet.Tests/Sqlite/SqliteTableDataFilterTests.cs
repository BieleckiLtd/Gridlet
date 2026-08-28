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
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
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
                Notes TEXT,
                InStock BOOLEAN,
                Code TEXT,
                Untyped
            );
            INSERT INTO Products (Name, Price, Notes, InStock, Code, Untyped) VALUES
                ('Widget', 5, 'in stock', 1, '007', 7),
                ('Wide widget', 50, NULL, 0, '7', 8),
                ('Gadget', 12.5, '50% off', 1, '008', 7),
                ('Gizmo', 100, 'discontinued', 0, '009', 9);
            CREATE TABLE FrequencyValues (frequency TEXT);
            INSERT INTO FrequencyValues (frequency) VALUES ('b'), ('a');
            CREATE VIRTUAL TABLE SearchProducts USING fts5(Value);
            INSERT INTO SearchProducts (Value) VALUES ('Widget');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
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
    /// A numeric column compares as a number. SQLite does most of this itself - it converts an
    /// untyped parameter to the column's affinity - so this pins the behaviour rather than the fix.
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

    /// <summary>
    /// A column declared with no type at all - ordinary in SQLite - has no affinity, so SQLite
    /// converts nothing and the text '7' never equals the stored number 7. This is the one case
    /// where the filter has to bind the value as a number itself.
    /// </summary>
    [Fact]
    public async Task A_column_with_no_declared_type_still_matches_a_number()
        => Assert.Equal(["Widget", "Gadget"], await NamesAsync(
            new TableDataFilter("Untyped", FilterOperator.Equals, "7")));

    /// <summary>
    /// The opposite guard: a text column keeps comparing as text, so a code written with leading
    /// zeros matches itself rather than the number it looks like.
    /// </summary>
    [Fact]
    public async Task A_text_column_is_not_turned_into_a_number()
    {
        Assert.Equal(["Widget"], await NamesAsync(
            new TableDataFilter("Code", FilterOperator.Equals, "007")));
        Assert.Equal(["Wide widget"], await NamesAsync(
            new TableDataFilter("Code", FilterOperator.Equals, "7")));
    }

    /// <summary>
    /// SQLite has no BOOLEAN type: it takes numeric affinity, so the column holds the integers 0 and
    /// 1 and the declared name says nothing on its own.
    /// </summary>
    [Fact]
    public async Task A_type_sqlite_does_not_know_is_compared_by_its_affinity()
        => Assert.Equal(["Widget", "Gadget"], await NamesAsync(
            new TableDataFilter("InStock", FilterOperator.Equals, "1")));

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
    public async Task A_column_profile_reports_exact_null_distinct_range_and_frequency_values()
    {
        var profile = await data.GetColumnProfileAsync(
            context, "main", "Products", new ColumnProfileRequest("Notes", 10));

        Assert.Equal("Notes", profile.Column);
        Assert.Equal("TEXT", profile.DataType);
        Assert.Equal(4, profile.TotalCount);
        Assert.Equal(1, profile.NullCount);
        Assert.Equal(3, profile.DistinctCount);
        Assert.Equal("50% off", profile.Minimum);
        Assert.Equal("in stock", profile.Maximum);
        Assert.Equal(4, profile.TopValues.Count);
        Assert.Contains(profile.TopValues, value => value.Value is null && value.Count == 1);
        Assert.Contains(profile.TopValues, value => Equals(value.Value, "discontinued") && value.Count == 1);
    }

    [Fact]
    public async Task A_column_profile_applies_filters_and_limits_top_values()
    {
        var profile = await data.GetColumnProfileAsync(
            context,
            "main",
            "Products",
            new ColumnProfileRequest(
                "Price",
                TopValues: 1,
                Filters: [new TableDataFilter("InStock", FilterOperator.Equals, "1")]));

        Assert.Equal(2, profile.TotalCount);
        Assert.Equal(0, profile.NullCount);
        Assert.Equal(2, profile.DistinctCount);
        Assert.Equal(5d, Convert.ToDouble(profile.Minimum));
        Assert.Equal(12.5d, Convert.ToDouble(profile.Maximum));
        var top = Assert.Single(profile.TopValues);
        Assert.Equal(1, top.Count);
    }

    [Fact]
    public async Task A_column_named_frequency_uses_its_value_as_the_profile_tie_breaker()
    {
        var profile = await data.GetColumnProfileAsync(
            context, "main", "FrequencyValues", new ColumnProfileRequest("frequency", 10));

        Assert.Equal(["a", "b"], profile.TopValues.Select(value => value.Value).ToArray());
        Assert.All(profile.TopValues, value => Assert.Equal(1, value.Count));
    }

    [Fact]
    public async Task A_column_profile_rejects_an_unknown_column()
        => await Assert.ThrowsAsync<GridletValidationException>(() => data.GetColumnProfileAsync(
            context, "main", "Products", new ColumnProfileRequest("Notes; DROP TABLE Products")));

    [Fact]
    public async Task A_column_profile_rejects_filters_on_hidden_virtual_table_columns()
        => await Assert.ThrowsAsync<GridletValidationException>(() => data.GetColumnProfileAsync(
            context,
            "main",
            "SearchProducts",
            new ColumnProfileRequest(
                "Value", Filters: [new TableDataFilter("SearchProducts", FilterOperator.Equals, "1")])));

    [Fact]
    public async Task An_unknown_filter_column_is_rejected()
        => await Assert.ThrowsAsync<GridletValidationException>(() => data.GetPageAsync(
            context, "main", "Products",
            new TableDataRequest(1, 50, null, SortDirection.Ascending,
                [new TableDataFilter("Name\"; DROP TABLE Products --", FilterOperator.Equals, "x")])));
}
