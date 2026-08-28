using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerSqlBuilderTests
{
    [Fact]
    public void Page_sql_without_sort_uses_stable_placeholder_order()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql("dbo", "Customers", null, SortDirection.Ascending);

        Assert.Equal(
            "SELECT * FROM [dbo].[Customers] ORDER BY (SELECT NULL) OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            sql);
    }

    [Fact]
    public void Page_sql_with_sort_quotes_the_column()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql("dbo", "Customers", "LastName", SortDirection.Descending);

        Assert.Equal(
            "SELECT * FROM [dbo].[Customers] ORDER BY [LastName] DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            sql);
    }

    [Fact]
    public void Page_sql_without_sort_uses_composite_primary_key_order()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql(
            "sales", "OrderLines", null, SortDirection.Ascending, ["OrderId", "LineNo"]);

        Assert.Equal(
            "SELECT * FROM [sales].[OrderLines] ORDER BY [OrderId] ASC, [LineNo] ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            sql);
    }

    [Fact]
    public void Page_sql_appends_remaining_primary_key_columns_as_sort_tie_breakers()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql(
            "dbo", "Customers", "LastName", SortDirection.Descending, ["TenantId", "CustomerId"]);

        Assert.Equal(
            "SELECT * FROM [dbo].[Customers] ORDER BY [LastName] DESC, [TenantId] ASC, [CustomerId] ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            sql);
    }

    [Fact]
    public void Page_sql_does_not_repeat_an_explicit_primary_key_sort_column()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql(
            "dbo", "Customers", "tenantid", SortDirection.Descending, ["TenantId", "CustomerId"]);

        Assert.Equal(
            "SELECT * FROM [dbo].[Customers] ORDER BY [tenantid] DESC, [CustomerId] ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            sql);
    }

    [Fact]
    public void Page_sql_neutralises_hostile_identifiers()
    {
        var sql = SqlServerSqlBuilder.BuildPageSql("dbo", "x]; DROP TABLE y; --", null, SortDirection.Ascending);

        Assert.Contains("[x]]; DROP TABLE y; --]", sql);
        Assert.DoesNotContain("[x]; DROP", sql);
    }

    [Fact]
    public void Count_sql_targets_quoted_object()
    {
        Assert.Equal(
            "SELECT COUNT_BIG(*) FROM [sales].[Orders];",
            SqlServerSqlBuilder.BuildCountSql("sales", "Orders"));
    }

    [Theory]
    [InlineData("int", true, true)]
    [InlineData("bit", true, false)]
    [InlineData("timestamp", true, false)]
    [InlineData("sql_variant", true, true)]
    [InlineData("varbinary", true, false)]
    [InlineData("xml", false, false)]
    [InlineData("geography", false, false)]
    [InlineData("custom_clr_type", false, false)]
    [InlineData(null, false, false)]
    public void Profile_capabilities_allow_only_known_comparable_system_types(
        string? systemType,
        bool canGroup,
        bool canRange)
    {
        Assert.Equal(
            (canGroup, canRange),
            SqlServerSqlBuilder.GetProfileCapabilities(systemType));
    }

    [Fact]
    public void Profile_aggregate_sql_quotes_identifiers_and_respects_capabilities()
    {
        var sql = SqlServerSqlBuilder.BuildProfileAggregateSql(
            "audit", "Odd]Table", "Value]Column", " WHERE [Enabled] = @f0",
            canGroup: false, canRange: false);

        Assert.Equal(
            "SELECT COUNT_BIG(*), COUNT_BIG([Value]]Column]), CAST(NULL AS bigint), " +
            "CAST(NULL AS nvarchar(1)), CAST(NULL AS nvarchar(1)) FROM " +
            "[audit].[Odd]]Table] WHERE [Enabled] = @f0;",
            sql);
    }

    [Fact]
    public void Profile_top_values_sql_uses_value_as_a_deterministic_tie_breaker()
    {
        var sql = SqlServerSqlBuilder.BuildProfileTopValuesSql(
            "dbo", "Customers", "Status", " WHERE [Active] = @f0");

        Assert.Equal(
            "SELECT TOP (@topValues) [Status], COUNT_BIG(*) AS frequency " +
            "FROM [dbo].[Customers] WHERE [Active] = @f0 GROUP BY [Status] " +
            "ORDER BY frequency DESC, [Status] ASC;",
            sql);
    }
}
