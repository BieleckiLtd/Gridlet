using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class PublishedEndpointTests
{
    private static Task<HttpResponseMessage> Publish(HttpClient client, object body)
        => client.PostAsJsonAsync("/gridlet/api/published", body);

    [Fact]
    public async Task Published_get_endpoint_executes_with_bound_parameters()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var publish = await Publish(client, new
        {
            name = "Top customers",
            method = "GET",
            route = "sales/top-customers",
            connectionName = "Main",
            database = "FakeDb",
            sql = "SELECT * FROM dbo.Customers WHERE Country = @country",
            parameters = new[] { new { name = "country", required = true } },
        });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var invoke = await client.GetAsync("/gridlet/pub/sales/top-customers?country=Poland");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        var body = await invoke.Content.ReadAsStringAsync();
        Assert.Contains("\"rows\"", body);
        Assert.Contains("42", body);
        Assert.Equal("Poland", fake.LastQueryParameters!["country"]);
    }

    [Fact]
    public async Task Missing_required_parameter_returns_400()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Needs a parameter",
            method = "GET",
            route = "needs-param",
            connectionName = "Main",
            sql = "SELECT @id",
            parameters = new[] { new { name = "id", required = true } },
        });

        var invoke = await client.GetAsync("/gridlet/pub/needs-param");

        Assert.Equal(HttpStatusCode.BadRequest, invoke.StatusCode);
        Assert.Contains("id", await invoke.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Declared_integer_parameters_are_bound_as_numbers()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        await Publish(client, new
        {
            name = "Paged customers",
            method = "GET",
            route = "customers/paged",
            connectionName = "Main",
            sql = "SELECT * FROM dbo.Customers ORDER BY CustomerId OFFSET ((@page - 1) * @page_size) ROWS FETCH NEXT @page_size ROWS ONLY",
            parameters = new[]
            {
                new { name = "page", required = true, type = "integer" },
                new { name = "page_size", required = true, type = "integer" },
            },
        });

        var invoke = await client.GetAsync("/gridlet/pub/customers/paged?page=2&page_size=10");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        Assert.Equal(2L, fake.LastQueryParameters!["page"]);
        Assert.Equal(10L, fake.LastQueryParameters["page_size"]);
    }

    [Fact]
    public async Task Invalid_typed_parameter_returns_400()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Paged customers", method = "GET", route = "typed",
            connectionName = "Main", sql = "SELECT @page",
            parameters = new[] { new { name = "page", required = true, type = "integer" } },
        });

        var invoke = await client.GetAsync("/gridlet/pub/typed?page=second");

        Assert.Equal(HttpStatusCode.BadRequest, invoke.StatusCode);
        Assert.Contains("integer", await invoke.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_or_disabled_routes_return_404()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Disabled",
            method = "GET",
            route = "disabled",
            connectionName = "Main",
            sql = "SELECT 1",
            enabled = false,
        });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/pub/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/pub/disabled")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_method_and_route_are_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = new { name = "One", method = "GET", route = "dup", connectionName = "Main", sql = "SELECT 1" };
        Assert.Equal(HttpStatusCode.OK, (await Publish(client, body)).StatusCode);

        var second = await Publish(client, body with { name = "Two" });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Hostile_routes_are_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await Publish(client, new
        {
            name = "Bad", method = "GET", route = "../escape", connectionName = "Main", sql = "SELECT 1",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_endpoint_makes_it_404()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var saved = await (await Publish(client, new
        {
            name = "Ephemeral", method = "GET", route = "ephemeral", connectionName = "Main", sql = "SELECT 1",
        })).Content.ReadFromJsonAsync<PublishedEndpoint>();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/gridlet/pub/ephemeral")).StatusCode);
        await client.DeleteAsync($"/gridlet/api/published/{saved!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/pub/ephemeral")).StatusCode);
    }
}
