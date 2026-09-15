using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

/// <summary>The spreadsheet-style column filter conditions, as SQL Server receives them.</summary>
public sealed class SqlServerColumnFilterTests
{
    private static readonly SqlServerFilterColumn[] Columns =
    [
        new("Id", "int"),
        new("Name", "nvarchar"),
        new("Price", "decimal"),
        new("Ratio", "float"),
        new("Active", "bit"),
        new("OrderedAt", "datetime2"),
    ];

    private static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) Build(
        params TableDataFilter[] filters)
        => SqlServerSqlBuilder.BuildFilterClause(filters, Columns, "dbo", "Products");

    [Fact]
    public void A_list_binds_each_value_as_the_column_type()
    {
        var (clause, parameters) = Build(new TableDataFilter("Id", FilterOperator.In) { Values = ["1", "2"] });

        Assert.Equal(" WHERE [Id] IN (@f0, @f1)", clause);
        Assert.Equal([("@f0", (object?)1L), ("@f1", 2L)], parameters);
    }

    [Fact]
    public void A_group_joins_its_conditions_in_parentheses_and_numbers_parameters_across_the_request()
    {
        var (clause, _) = Build(
            new TableDataFilter("Name", FilterOperator.AnyOf)
            {
                Conditions =
                [
                    new TableDataFilter("Name", FilterOperator.NotIn) { Values = ["Ada"] },
                    new TableDataFilter("Name", FilterOperator.IsBlank),
                ],
            },
            new TableDataFilter("Id", FilterOperator.GreaterThan, "3"));

        Assert.Equal(
            " WHERE ([Name] NOT IN (@f0) OR ([Name] IS NULL OR DATALENGTH([Name]) = 0)) AND [Id] > @f1",
            clause);
    }

    [Fact]
    public void A_group_of_one_condition_is_that_condition()
    {
        var (clause, _) = Build(new TableDataFilter("", FilterOperator.AllOf)
        {
            Conditions = [new TableDataFilter("Id", FilterOperator.IsNotNull)],
        });

        Assert.Equal(" WHERE [Id] IS NOT NULL", clause);
    }

    [Fact]
    public void An_empty_group_or_list_is_rejected()
    {
        Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("Name", FilterOperator.AnyOf) { Conditions = [] }));
        Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("Name", FilterOperator.In) { Values = [] }));
    }

    /// <summary>
    /// A blank in a text column is a null or a value with no characters. Only a text column can hold
    /// the empty text, and comparing a number column with it would convert '' to zero.
    /// </summary>
    [Theory]
    [InlineData("Name", "([Name] IS NULL OR DATALENGTH([Name]) = 0)", "DATALENGTH([Name]) > 0")]
    [InlineData("Id", "[Id] IS NULL", "[Id] IS NOT NULL")]
    public void A_blank_is_a_null_or_empty_text(string column, string blank, string notBlank)
    {
        Assert.Equal(" WHERE " + blank, Build(new TableDataFilter(column, FilterOperator.IsBlank)).Clause);
        Assert.Equal(" WHERE " + notBlank, Build(new TableDataFilter(column, FilterOperator.IsNotBlank)).Clause);
    }

    [Theory]
    [InlineData("a*b?c", "a%b_c")]
    [InlineData("50%~*", "50[%]*")]
    [InlineData("[x]_~~", "[[]x][_]~")]
    [InlineData("tail~", "tail~")]
    public void A_pattern_uses_spreadsheet_wildcards_and_matches_everything_else_literally(
        string pattern, string expected)
    {
        var (clause, parameters) = Build(new TableDataFilter("Name", FilterOperator.Matches, pattern));

        Assert.Equal(" WHERE [Name] LIKE @f0", clause);
        Assert.Equal(expected, Assert.Single(parameters).Value);
    }

    [Fact]
    public void The_negated_text_conditions_use_not_like()
    {
        var (clause, parameters) = Build(
            new TableDataFilter("Name", FilterOperator.NotStartsWith, "A"),
            new TableDataFilter("Name", FilterOperator.NotEndsWith, "z"),
            new TableDataFilter("Name", FilterOperator.NotMatches, "*x*"));

        Assert.Equal(" WHERE [Name] NOT LIKE @f0 AND [Name] NOT LIKE @f1 AND [Name] NOT LIKE @f2", clause);
        Assert.Equal(["A%", "%z", "%x%"], parameters.Select(parameter => parameter.Value).ToArray());
    }

    /// <summary>
    /// A number binds as a number, so 2.5 compares with an int column that cannot convert the text
    /// '2.5', and a date binds as a date, which the text is not under every SQL Server language.
    /// </summary>
    [Fact]
    public void Values_bind_as_the_column_type()
    {
        var (_, parameters) = Build(
            new TableDataFilter("Price", FilterOperator.GreaterThan, "1.5"),
            new TableDataFilter("Ratio", FilterOperator.LessThan, "0.25"),
            new TableDataFilter("Active", FilterOperator.Equals, "true"),
            new TableDataFilter("OrderedAt", FilterOperator.GreaterThanOrEqual, "2026-01-02"),
            new TableDataFilter("Id", FilterOperator.Equals, "2.5"),
            new TableDataFilter("Name", FilterOperator.Equals, "007"));

        Assert.Equal(
            new object?[] { 1.5m, 0.25d, true, new DateTime(2026, 1, 2), 2.5m, "007" },
            parameters.Select(parameter => parameter.Value).ToArray());
    }

    [Fact]
    public void A_value_that_is_not_the_column_type_is_rejected()
    {
        var exception = Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("OrderedAt", FilterOperator.Equals, "soon")));

        Assert.Contains("not a date", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Top_items_compare_with_the_value_at_that_rank_over_the_whole_object()
    {
        var (clause, parameters) = Build(new TableDataFilter("Price", FilterOperator.Top, "10"));

        Assert.Equal(
            " WHERE [Price] >= (SELECT MIN([ranked].[value]) FROM (SELECT TOP (@f0) [Price] AS [value] " +
            "FROM [dbo].[Products] WHERE [Price] IS NOT NULL ORDER BY [Price] DESC) AS [ranked])",
            clause);
        Assert.Equal(10L, Assert.Single(parameters).Value);
    }

    [Fact]
    public void Bottom_percent_uses_top_percent_in_ascending_order()
    {
        var (clause, parameters) = Build(new TableDataFilter("Price", FilterOperator.BottomPercent, "25"));

        Assert.StartsWith(" WHERE [Price] <= (SELECT MAX([ranked].[value])", clause, StringComparison.Ordinal);
        Assert.Contains("TOP (@f0) PERCENT", clause, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [Price] ASC", clause, StringComparison.Ordinal);
        Assert.Equal(25d, Assert.Single(parameters).Value);
    }

    [Theory]
    [InlineData(FilterOperator.Top, "0")]
    [InlineData(FilterOperator.Top, "501")]
    [InlineData(FilterOperator.Top, "ten")]
    [InlineData(FilterOperator.TopPercent, "101")]
    public void A_ranking_outside_the_spreadsheet_limits_is_rejected(FilterOperator @operator, string value)
        => Assert.Throws<GridletValidationException>(() => Build(new TableDataFilter("Price", @operator, value)));

    [Fact]
    public void An_average_is_computed_as_float_over_the_whole_object()
    {
        Assert.Equal(
            " WHERE CONVERT(float, [Price]) > (SELECT AVG(CONVERT(float, [Price])) FROM [dbo].[Products])",
            Build(new TableDataFilter("Price", FilterOperator.AboveAverage)).Clause);
        Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("Name", FilterOperator.BelowAverage)));
    }

    [Fact]
    public void Rankings_and_averages_need_the_object_they_read()
        => Assert.Throws<GridletValidationException>(() => SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Price", FilterOperator.Top, "10")], ["Price"]));

    [Fact]
    public void Month_and_quarter_match_in_any_year()
    {
        var (clause, parameters) = Build(
            new TableDataFilter("OrderedAt", FilterOperator.MonthEquals, "3"),
            new TableDataFilter("OrderedAt", FilterOperator.QuarterEquals, "2"));

        Assert.Equal(" WHERE MONTH([OrderedAt]) = @f0 AND DATEPART(quarter, [OrderedAt]) = @f1", clause);
        Assert.Equal(new object?[] { 3, 2 }, parameters.Select(parameter => parameter.Value).ToArray());
        Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("OrderedAt", FilterOperator.MonthEquals, "13")));
        Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("Id", FilterOperator.QuarterEquals, "1")));
    }

    [Fact]
    public void A_request_that_binds_more_values_than_sql_server_accepts_is_rejected()
    {
        var exception = Assert.Throws<GridletValidationException>(() => Build(
            new TableDataFilter("Name", FilterOperator.In)
            {
                Values = Enumerable.Range(0, 2001).Select(number => number.ToString()).ToArray(),
            }));

        Assert.Contains("2000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_checklist_reads_distinct_non_blank_values_and_whether_blanks_exist_in_the_same_scope()
    {
        var sql = SqlServerSqlBuilder.BuildFilterValuesSql(
            "dbo", "Products", Columns[1], " WHERE [Id] > @f0", search: true);

        Assert.Equal(
            "SELECT DISTINCT TOP (@limit) [Name] FROM [dbo].[Products] WHERE [Id] > @f0 AND DATALENGTH([Name]) > 0 " +
            "AND CONVERT(nvarchar(max), [Name]) LIKE @search ORDER BY [Name];\n" +
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM [dbo].[Products] WHERE [Id] > @f0 AND " +
            "([Name] IS NULL OR DATALENGTH([Name]) = 0)) THEN 1 ELSE 0 END;",
            sql);
        Assert.Equal("%50[%]%", SqlServerSqlBuilder.BuildFilterValuesSearch("50%"));
    }

    [Fact]
    public void Display_sql_substitutes_exact_parameter_tokens_with_sql_server_literals()
    {
        var sql = SqlServerSqlBuilder.SubstituteFilterParameters(
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
            "WHERE N'O''Brien' <> N'ten' AND '2026-01-02T03:04:05' = 1 "
            + "AND 12.50 = '04:05:06' AND 0 IS NULL",
            sql);
    }

    [Fact]
    public void Display_sql_follows_the_filter_with_the_sort_on_its_own_line()
    {
        Assert.Equal(
            "WHERE [Id] > 3\nORDER BY [Name] DESC",
            SqlServerSqlBuilder.BuildFilterDisplaySql(
                [new TableDataFilter("Id", FilterOperator.GreaterThan, "3")],
                Columns, "dbo", "Products", "name", SortDirection.Descending));
        Assert.Equal(
            "ORDER BY [Price] ASC",
            SqlServerSqlBuilder.BuildFilterDisplaySql(
                null, Columns, "dbo", "Products", "Price", SortDirection.Ascending));
        Assert.Throws<GridletValidationException>(() => SqlServerSqlBuilder.BuildFilterDisplaySql(
            null, Columns, "dbo", "Products", "Missing", SortDirection.Ascending));
    }

    /// <summary>
    /// "Use in query" runs the displayed clause, so a date has to be text every SQL Server date type
    /// accepts. datetime rejects more than three fractional digits; only a datetime2 value can have
    /// finer ticks, and datetime2 accepts all seven.
    /// </summary>
    [Fact]
    public void Display_sql_writes_dates_that_datetime_and_datetime2_both_accept()
    {
        var sql = SqlServerSqlBuilder.SubstituteFilterParameters(
            "@f0 @f1 @f2",
            [
                ("@f0", new DateTime(2026, 1, 2)),
                ("@f1", new DateTime(2026, 1, 2, 3, 4, 5, 123)),
                ("@f2", new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1_234_567)),
            ]);

        Assert.Equal("'2026-01-02T00:00:00' '2026-01-02T03:04:05.123' '2026-01-02T03:04:05.1234567'", sql);
    }
}
