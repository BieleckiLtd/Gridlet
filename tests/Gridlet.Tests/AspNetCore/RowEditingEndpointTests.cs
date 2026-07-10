using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class RowEditingEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers";

    [Fact]
    public async Task Insert_update_delete_reach_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        // Dictionaries, not anonymous objects: column names must reach the server verbatim
        // (JSON property names of anonymous types get camel-cased), matching what the UI sends.
        var insert = await client.PostAsJsonAsync($"{Base}/rows",
            new { values = new Dictionary<string, object?> { ["FirstName"] = "Ada", ["Age"] = 36 } });
        var update = await client.PostAsJsonAsync($"{Base}/rows/update",
            new
            {
                key = new Dictionary<string, object?> { ["CustomerId"] = 7 },
                values = new Dictionary<string, object?> { ["FirstName"] = "Grace" },
            });
        var delete = await client.PostAsJsonAsync($"{Base}/rows/delete",
            new { key = new Dictionary<string, object?> { ["CustomerId"] = 7 } });

        Assert.Equal(HttpStatusCode.OK, insert.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Contains("insert dbo.Customers (FirstName,Age)", fake.Calls);
        Assert.Contains("update dbo.Customers key(CustomerId) set(FirstName)", fake.Calls);
        Assert.Contains("delete dbo.Customers key(CustomerId)", fake.Calls);
    }

    [Fact]
    public async Task Empty_values_are_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/rows", new { values = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Writes_are_forbidden_when_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, c => c.AllowWrites = false);
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/rows",
            new { values = new { FirstName = "Ada" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
