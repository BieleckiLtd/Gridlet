using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerFilterClauseTests
{
    private static readonly string[] Columns = ["Id", "Name", "Notes"];

    [Fact]
    public void No_filters_means_no_clause()
    {
        var (clause, parameters) = SqlServerSqlBuilder.BuildFilterClause(null, Columns);

        Assert.Equal("", clause);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Conditions_are_combined_with_and_and_numbered_apart()
    {
        var (clause, parameters) = SqlServerSqlBuilder.BuildFilterClause(
            [
                new TableDataFilter("Id", FilterOperator.GreaterThan, "10"),
                new TableDataFilter("Name", FilterOperator.Equals, "Ada"),
            ],
            Columns);

        Assert.Equal(" WHERE [Id] > @f0 AND [Name] = @f1", clause);
        Assert.Equal([("@f0", (object?)"10"), ("@f1", "Ada")], parameters);
    }

    [Theory]
    [InlineData(FilterOperator.Contains, "[Name] LIKE @f0", "%ada%")]
    [InlineData(FilterOperator.NotContains, "[Name] NOT LIKE @f0", "%ada%")]
    [InlineData(FilterOperator.StartsWith, "[Name] LIKE @f0", "ada%")]
    [InlineData(FilterOperator.EndsWith, "[Name] LIKE @f0", "%ada")]
    public void Text_matching_uses_LIKE_with_the_pattern_as_a_parameter(
        FilterOperator @operator, string expectedPredicate, string expectedValue)
    {
        var (clause, parameters) = SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Name", @operator, "ada")], Columns);

        Assert.Equal(" WHERE " + expectedPredicate, clause);
        Assert.Equal(expectedValue, Assert.Single(parameters).Value);
    }

    /// <summary>
    /// A value containing LIKE wildcards has to match those characters literally, or searching for
    /// "50%" would match every row. Each one becomes a character class, which is the form SQL Server
    /// documents: an opening bracket starts a class of its own, so there is no reading of the escape
    /// rules under which <c>[[]</c> is anything but one literal bracket. A closing bracket outside a
    /// class is already literal, and a backslash is now just a character in the search text.
    /// </summary>
    [Fact]
    public void Wildcards_in_the_value_are_escaped()
    {
        var (_, parameters) = SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Notes", FilterOperator.Contains, @"50% [a_b] \x")], Columns);

        Assert.Equal(@"%50[%] [[]a[_]b] \x%", Assert.Single(parameters).Value);
    }

    [Fact]
    public void Null_checks_need_no_parameter()
    {
        var (clause, parameters) = SqlServerSqlBuilder.BuildFilterClause(
            [
                new TableDataFilter("Notes", FilterOperator.IsNull),
                new TableDataFilter("Name", FilterOperator.IsNotNull),
            ],
            Columns);

        Assert.Equal(" WHERE [Notes] IS NULL AND [Name] IS NOT NULL", clause);
        Assert.Empty(parameters);
    }

    [Fact]
    public void A_column_the_object_does_not_have_is_rejected()
        => Assert.Throws<GridletValidationException>(() => SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Name]; DROP TABLE Customers --", FilterOperator.Equals, "x")], Columns));

    [Fact]
    public void The_column_name_is_taken_from_the_object_rather_than_the_request()
    {
        var (clause, _) = SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("nAmE", FilterOperator.Equals, "Ada")], Columns);

        Assert.Equal(" WHERE [Name] = @f0", clause);
    }

    [Fact]
    public void A_comparison_without_a_value_says_to_use_the_null_check_instead()
    {
        var exception = Assert.Throws<GridletValidationException>(() => SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Name", FilterOperator.Equals)], Columns));

        Assert.Contains("is null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clause_reaches_both_the_page_and_the_count()
    {
        var (clause, _) = SqlServerSqlBuilder.BuildFilterClause(
            [new TableDataFilter("Name", FilterOperator.Equals, "Ada")], Columns);

        Assert.Contains(
            "FROM [dbo].[Customers] WHERE [Name] = @f0 ORDER BY",
            SqlServerSqlBuilder.BuildPageSql("dbo", "Customers", "Id", SortDirection.Ascending, null, clause),
            StringComparison.Ordinal);
        Assert.Equal(
            "SELECT COUNT_BIG(*) FROM [dbo].[Customers] WHERE [Name] = @f0;",
            SqlServerSqlBuilder.BuildCountSql("dbo", "Customers", clause));
    }
}
