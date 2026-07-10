using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerDmlBuilderTests
{
    [Fact]
    public void Insert_quotes_columns_and_parameterises_values()
    {
        var sql = SqlServerDmlBuilder.BuildInsert("dbo", "Customers", ["FirstName", "Last]Name"]);

        Assert.Equal(
            "INSERT INTO [dbo].[Customers] ([FirstName], [Last]]Name]) VALUES (@v0, @v1);",
            sql);
    }

    [Fact]
    public void Update_sets_values_and_filters_by_key()
    {
        var sql = SqlServerDmlBuilder.BuildUpdate("dbo", "Customers", ["FirstName", "Email"], ["CustomerId"]);

        Assert.Equal(
            "UPDATE [dbo].[Customers] SET [FirstName] = @v0, [Email] = @v1 WHERE [CustomerId] = @k0;",
            sql);
    }

    [Fact]
    public void Delete_supports_composite_keys()
    {
        var sql = SqlServerDmlBuilder.BuildDelete("sales", "OrderLines", ["OrderId", "LineNo"]);

        Assert.Equal(
            "DELETE FROM [sales].[OrderLines] WHERE [OrderId] = @k0 AND [LineNo] = @k1;",
            sql);
    }
}
