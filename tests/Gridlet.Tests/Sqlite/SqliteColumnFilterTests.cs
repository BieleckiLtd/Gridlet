using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// The spreadsheet-style column filter conditions, run by SQLite against a real database. Dates are
/// ISO 8601 text, the way SQLite applications usually keep them.
/// </summary>
public sealed class SqliteColumnFilterTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-column-filter-{Guid.NewGuid():N}.db");
    private readonly SqliteTableDataService data = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "ColumnFilter",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                Customer TEXT NOT NULL,
                Total NUMERIC,
                OrderedAt TEXT,
                Notes TEXT
            );
            INSERT INTO Orders (Customer, Total, OrderedAt, Notes) VALUES
                ('Ada', 10, '2026-01-05T09:30:00.000Z', 'rush'),
                ('Grace', 40, '2026-02-14T18:00:00.000Z', ''),
                ('Alan', 40, '2026-04-01T08:00:00.000Z', NULL),
                ('Edsger', 25, '2025-11-30T23:59:59.000Z', 'a*b'),
                ('Barbara', 5, '2026-07-19T12:00:00.000Z', 'gift?');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    private async Task<long[]> IdsAsync(params TableDataFilter[] filters)
    {
        var page = await data.GetPageAsync(
            context, "main", "Orders", new TableDataRequest(1, 50, "Id", SortDirection.Ascending, filters));
        return page.Rows.Select(row => Convert.ToInt64(row[0])).ToArray();
    }

    private static TableDataFilter AnyOf(params TableDataFilter[] conditions)
        => new("", FilterOperator.AnyOf) { Conditions = conditions };

    private static TableDataFilter AllOf(params TableDataFilter[] conditions)
        => new("", FilterOperator.AllOf) { Conditions = conditions };

    [Fact]
    public async Task A_checklist_with_blanks_matches_the_listed_values_nulls_and_empty_text()
        => Assert.Equal(new long[] { 1, 2, 3 }, await IdsAsync(AnyOf(
            new TableDataFilter("Notes", FilterOperator.In) { Values = ["rush"] },
            new TableDataFilter("Notes", FilterOperator.IsBlank))));

    [Fact]
    public async Task An_excluding_checklist_without_blanks_drops_the_listed_values_and_the_blanks()
        => Assert.Equal(new long[] { 4, 5 }, await IdsAsync(AllOf(
            new TableDataFilter("Customer", FilterOperator.NotIn) { Values = ["Ada", "Grace"] },
            new TableDataFilter("Notes", FilterOperator.IsNotBlank))));

    [Fact]
    public async Task A_pattern_matches_with_spreadsheet_wildcards_and_the_tilde_escape()
    {
        Assert.Equal(new long[] { 1, 3 }, await IdsAsync(new TableDataFilter("Customer", FilterOperator.Matches, "a*")));
        Assert.Equal(new long[] { 1 }, await IdsAsync(new TableDataFilter("Customer", FilterOperator.Matches, "?da")));
        Assert.Equal(new long[] { 4 }, await IdsAsync(new TableDataFilter("Notes", FilterOperator.Matches, "a~*b")));
        Assert.Equal(new long[] { 5 }, await IdsAsync(new TableDataFilter("Notes", FilterOperator.Matches, "gift~?")));
        Assert.Equal(new long[] { 2, 4, 5 }, await IdsAsync(new TableDataFilter("Customer", FilterOperator.NotStartsWith, "A")));
    }

    /// <summary>The totals are 10, 40, 40, 25 and 5, so the second largest ties with the first.</summary>
    [Fact]
    public async Task Top_and_bottom_items_keep_every_row_that_ties_with_the_last_rank()
    {
        Assert.Equal(new long[] { 2, 3 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.Top, "2")));
        Assert.Equal(new long[] { 2, 3, 4 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.Top, "3")));
        Assert.Equal(new long[] { 1, 5 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.Bottom, "2")));
    }

    [Fact]
    public async Task A_percentage_ranking_rounds_its_row_count_up()
    {
        Assert.Equal(new long[] { 2, 3 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.TopPercent, "20")));
        Assert.Equal(new long[] { 1, 5 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.BottomPercent, "40")));
    }

    /// <summary>
    /// A spreadsheet ranks the whole column, not the rows other filters leave, so the largest total
    /// stays 40 even though neither remaining customer has it.
    /// </summary>
    [Fact]
    public async Task A_ranking_ignores_the_other_filters()
        => Assert.Empty(await IdsAsync(
            new TableDataFilter("Customer", FilterOperator.In) { Values = ["Ada", "Edsger"] },
            new TableDataFilter("Total", FilterOperator.Top, "1")));

    [Fact]
    public async Task Averages_compare_with_the_mean_of_the_whole_table()
    {
        Assert.Equal(new long[] { 2, 3, 4 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.AboveAverage)));
        Assert.Equal(new long[] { 1, 5 }, await IdsAsync(new TableDataFilter("Total", FilterOperator.BelowAverage)));
    }

    [Fact]
    public async Task Months_and_quarters_of_iso_text_dates_match_in_any_year()
    {
        Assert.Equal(new long[] { 2 }, await IdsAsync(new TableDataFilter("OrderedAt", FilterOperator.MonthEquals, "2")));
        Assert.Equal(new long[] { 1, 2 }, await IdsAsync(new TableDataFilter("OrderedAt", FilterOperator.QuarterEquals, "1")));
        Assert.Equal(new long[] { 4 }, await IdsAsync(new TableDataFilter("OrderedAt", FilterOperator.QuarterEquals, "4")));
    }

    /// <summary>A day bound without a time orders correctly against every stored time of that day.</summary>
    [Fact]
    public async Task A_date_range_with_day_bounds_covers_the_whole_of_each_day()
        => Assert.Equal(new long[] { 1, 2 }, await IdsAsync(AllOf(
            new TableDataFilter("OrderedAt", FilterOperator.GreaterThanOrEqual, "2026-01-05"),
            new TableDataFilter("OrderedAt", FilterOperator.LessThan, "2026-02-15"))));

    [Fact]
    public async Task The_checklist_lists_distinct_values_in_order_and_reports_blanks_apart()
    {
        var notes = await data.GetColumnFilterValuesAsync(
            context, "main", "Orders", new ColumnFilterValuesRequest("Notes"));
        var totals = await data.GetColumnFilterValuesAsync(
            context, "main", "Orders", new ColumnFilterValuesRequest("total"));

        Assert.Equal(new object?[] { "a*b", "gift?", "rush" }, notes.Values);
        Assert.True(notes.HasBlanks);
        Assert.False(notes.IsTruncated);
        Assert.Equal(new object?[] { 5L, 10L, 25L, 40L }, totals.Values);
        Assert.False(totals.HasBlanks);
    }

    [Fact]
    public async Task The_checklist_applies_the_other_filters_its_search_and_its_limit()
    {
        var filtered = await data.GetColumnFilterValuesAsync(context, "main", "Orders",
            new ColumnFilterValuesRequest(
                "Notes", [new TableDataFilter("Customer", FilterOperator.In) { Values = ["Ada", "Edsger"] }]));
        var searched = await data.GetColumnFilterValuesAsync(context, "main", "Orders",
            new ColumnFilterValuesRequest("Notes", Search: "?"));
        var limited = await data.GetColumnFilterValuesAsync(context, "main", "Orders",
            new ColumnFilterValuesRequest("Customer", Limit: 2));

        Assert.Equal(new object?[] { "a*b", "rush" }, filtered.Values);
        Assert.False(filtered.HasBlanks);
        Assert.Equal(new object?[] { "gift?" }, searched.Values);
        Assert.Equal(new object?[] { "Ada", "Alan" }, limited.Values);
        Assert.True(limited.IsTruncated);
    }

    [Fact]
    public async Task The_described_sql_follows_the_filter_with_the_sort()
    {
        Assert.Equal(
            "WHERE \"Total\" > 10\nORDER BY \"Customer\" DESC",
            await data.GetFilterSqlAsync(context, "main", "Orders",
                [new TableDataFilter("Total", FilterOperator.GreaterThan, "10")],
                "customer", SortDirection.Descending));
        Assert.Equal(
            "ORDER BY \"OrderedAt\" ASC",
            await data.GetFilterSqlAsync(context, "main", "Orders", null, "OrderedAt", SortDirection.Ascending));
        await Assert.ThrowsAsync<GridletValidationException>(() => data.GetFilterSqlAsync(
            context, "main", "Orders", null, "Missing", SortDirection.Ascending));
    }

    [Fact]
    public async Task The_checklist_rejects_a_column_the_table_does_not_have()
        => await Assert.ThrowsAsync<GridletValidationException>(() => data.GetColumnFilterValuesAsync(
            context, "main", "Orders", new ColumnFilterValuesRequest("Notes; DROP TABLE Orders")));

    [Fact]
    public void Display_sql_substitutes_exact_parameter_tokens_with_sqlite_literals()
    {
        var sql = SqliteFilterBuilder.SubstituteFilterParameters(
            "WHERE @f1 <> @f10 AND @f2 = @f3 AND @f4 = @f5 AND @f6 IS @f7",
            [
                ("@f1", "O'Brien"),
                ("@f10", "ten"),
                ("@f2", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
                ("@f3", true),
                ("@f4", 12.50m),
                ("@f5", new TimeSpan(4, 5, 6)),
                ("@f6", false),
                ("@f7", (object?)null),
            ]);

        Assert.Equal(
            "WHERE 'O''Brien' <> 'ten' AND '2026-01-02T03:04:05.0000000Z' = 1 "
            + "AND 12.50 = '04:05:06' AND 0 IS NULL",
            sql);
    }
}
