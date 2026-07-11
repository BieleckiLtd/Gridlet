using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Published_write_methods_bind_json_body_parameters(string method)
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var publish = await Publish(client, new
        {
            name = $"{method} customer",
            method,
            route = $"customers/{method.ToLowerInvariant()}",
            connectionName = "Main",
            sql = "SELECT @status",
            parameters = new[] { new { name = "status", required = true } },
        });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        using var request = new HttpRequestMessage(
            new HttpMethod(method), $"/gridlet/pub/customers/{method.ToLowerInvariant()}")
        {
            Content = JsonContent.Create(new { status = "updated" }),
        };
        var invoke = await client.SendAsync(request);

        Assert.True(invoke.IsSuccessStatusCode, await invoke.Content.ReadAsStringAsync());
        Assert.Equal("updated", fake.LastQueryParameters!["status"]);
    }

    [Fact]
    public async Task Published_endpoint_streams_rows_with_a_trailing_row_count_and_no_truncated_field()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Answers", method = "GET", route = "answers", connectionName = "Main", sql = "SELECT 42",
        });

        var invoke = await client.GetAsync("/gridlet/pub/answers");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        Assert.Equal("application/json", invoke.Content.Headers.ContentType!.MediaType);
        using var doc = JsonDocument.Parse(await invoke.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(42, root.GetProperty("rows")[0].GetProperty("Answer").GetInt32());
        Assert.Equal(1, root.GetProperty("rowCount").GetInt32());
        Assert.False(root.TryGetProperty("truncated", out var truncated));
    }

    [Fact]
    public async Task Published_endpoint_is_uncapped_by_default()
    {
        // Global default is set low, but published endpoints no longer fall back to it.
        var (app, client) = await StartWithMaxRows(250);
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        await Publish(client, new
        {
            name = "Default cap", method = "GET", route = "default-cap", connectionName = "Main", sql = "SELECT 42",
        });
        await client.GetAsync("/gridlet/pub/default-cap");

        Assert.Equal(0, fake.LastQueryOptions!.MaxRowsPerResultSet);
    }

    [Fact]
    public async Task Null_max_rows_streams_more_rows_than_the_global_default()
    {
        // Global default is 10,000; a published endpoint with no MaxRows must stream past it.
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Everything", method = "GET", route = "everything", connectionName = "Main", sql = "many:15000",
        });

        var invoke = await client.GetAsync("/gridlet/pub/everything");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        using var doc = JsonDocument.Parse(await invoke.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(15_000, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(15_000, root.GetProperty("rowCount").GetInt32());
        Assert.False(root.TryGetProperty("truncated", out var truncated));
    }

    [Fact]
    public async Task Per_endpoint_max_rows_override_can_exceed_the_global_cap()
    {
        var (app, client) = await StartWithMaxRows(250);
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        await Publish(client, new
        {
            name = "Big", method = "GET", route = "big", connectionName = "Main", sql = "SELECT 42", maxRows = 100_000,
        });
        await client.GetAsync("/gridlet/pub/big");

        Assert.Equal(100_000, fake.LastQueryOptions!.MaxRowsPerResultSet);
    }

    [Fact]
    public async Task Max_rows_zero_streams_uncapped()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        await Publish(client, new
        {
            name = "All", method = "GET", route = "all", connectionName = "Main", sql = "SELECT 42", maxRows = 0,
        });
        await client.GetAsync("/gridlet/pub/all");

        Assert.Equal(0, fake.LastQueryOptions!.MaxRowsPerResultSet);
    }

    [Fact]
    public async Task Negative_max_rows_is_rejected_at_publish()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var publish = await Publish(client, new
        {
            name = "Bad cap", method = "GET", route = "bad-cap", connectionName = "Main", sql = "SELECT 1", maxRows = -5,
        });

        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task Failure_before_streaming_returns_a_clean_400()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Early boom", method = "GET", route = "early-boom", connectionName = "Main", sql = "boom",
        });

        var invoke = await client.GetAsync("/gridlet/pub/early-boom");

        Assert.Equal(HttpStatusCode.BadRequest, invoke.StatusCode);
        Assert.Contains("kaboom", await invoke.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Failure_after_streaming_starts_keeps_200_and_emits_an_error_marker()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Halfway", method = "GET", route = "halfway", connectionName = "Main", sql = "stream-boom",
        });

        var invoke = await client.GetAsync("/gridlet/pub/halfway");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        using var doc = JsonDocument.Parse(await invoke.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("rows").GetArrayLength());
        Assert.Equal("mid-stream kaboom", root.GetProperty("error").GetString());
    }

    private static Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)> StartWithMaxRows(int maxRows)
        => GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=secret-host;Database=hidden;", FakeGridletProvider.Name);
            o.Limits.MaxQueryResultRows = maxRows;
            o.Security.AllowAnonymous = true;
        });

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
