using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class DdlEndpointTests
{
    private const string Db = "/gridlet/api/connections/Main/databases/FakeDb";

    [Fact]
    public async Task Table_and_column_operations_reach_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var create = await client.PostAsJsonAsync($"{Db}/tables", new
        {
            schema = "dbo",
            name = "Widgets",
            columns = new[] { new { name = "Id", dataType = "int", isPrimaryKey = true, isIdentity = true, isNullable = false } },
        });
        var addColumn = await client.PostAsJsonAsync($"{Db}/objects/dbo/Widgets/columns",
            new { name = "Age", dataType = "int", isNullable = true });
        var alterColumn = await client.PutAsJsonAsync($"{Db}/objects/dbo/Widgets/columns/Age",
            new { name = "Years", dataType = "bigint", isNullable = false });
        var dropColumn = await client.DeleteAsync($"{Db}/objects/dbo/Widgets/columns/Years");
        var dropTable = await client.DeleteAsync($"{Db}/objects/dbo/Widgets");

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alterColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropTable.StatusCode);
        Assert.Contains("createTable dbo.Widgets (1 columns)", fake.Calls);
        Assert.Contains("addColumn dbo.Widgets.Age", fake.Calls);
        Assert.Contains("alterColumn dbo.Widgets.Age -> Years", fake.Calls);
        Assert.Contains("dropColumn dbo.Widgets.Years", fake.Calls);
        Assert.Contains("dropTable dbo.Widgets", fake.Calls);
    }

    [Fact]
    public async Task Ddl_is_forbidden_when_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, c => c.AllowDdl = false);
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var create = await client.PostAsJsonAsync($"{Db}/tables", new
        {
            schema = "dbo",
            name = "Widgets",
            columns = new[] { new { name = "Id", dataType = "int" } },
        });
        var dropTable = await client.DeleteAsync($"{Db}/objects/dbo/Widgets");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dropTable.StatusCode);
    }
}
