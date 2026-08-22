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

    /// <summary>
    /// The segment published endpoints answer on is the host's choice, so a Gridlet can be fitted
    /// to an application's own URL conventions. Only the segment moves: the routes themselves are
    /// whatever was published.
    /// </summary>
    [Fact]
    public async Task Published_endpoints_answer_on_the_configured_route_prefix()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
            options.PublishedApiRoutePrefix = "endpoints";
        });
        await using var _ = app;

        var publish = await Publish(client, new
        {
            name = "Customers",
            method = "GET",
            route = "customers",
            connectionName = "Main",
            database = "FakeDb",
            sql = "SELECT * FROM dbo.Customers",
        });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var moved = await client.GetAsync("/gridlet/endpoints/customers");
        var original = await client.GetAsync("/gridlet/pub/customers");

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Contains("\"rows\"", await moved.Content.ReadAsStringAsync());
        // The default segment is not kept alive alongside the configured one; there is one address.
        Assert.Equal(HttpStatusCode.NotFound, original.StatusCode);
    }

    /// <summary>The browser builds published URLs from the server's answer, never from the default.</summary>
    [Fact]
    public async Task Meta_reports_the_configured_route_prefix_to_the_browser()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
            options.PublishedApiRoutePrefix = "/endpoints/";
        });
        await using var _ = app;

        var meta = await client.GetFromJsonAsync<JsonElement>("/gridlet/api/meta");

        // Surrounding slashes are a normal way to write a route prefix, and are normalized away.
        Assert.Equal("endpoints", meta.GetProperty("publishedApiSegment").GetString());
    }

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

    [Fact]
    public async Task Unexpected_failure_before_streaming_is_sanitized()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Unexpected", method = "GET", route = "unexpected",
            connectionName = "Main", sql = "unexpected-boom",
        });

        var invoke = await client.GetAsync("/gridlet/pub/unexpected");
        var body = await invoke.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, invoke.StatusCode);
        Assert.Contains("unexpected server error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_PUBLISHED_SENTINEL", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ndjson_accept_header_streams_row_and_completion_events()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "NDJSON answers", method = "GET", route = "ndjson-answers",
            connectionName = "Main", sql = "SELECT 42",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/pub/ndjson-answers");
        request.Headers.Accept.ParseAdd("application/x-ndjson");
        var invoke = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        Assert.Equal("application/x-ndjson", invoke.Content.Headers.ContentType!.MediaType);
        var lines = (await invoke.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var rowEvent = JsonDocument.Parse(lines[0]);
        Assert.Equal("row", rowEvent.RootElement.GetProperty("type").GetString());
        Assert.Equal(42, rowEvent.RootElement.GetProperty("row").GetProperty("Answer").GetInt32());

        using var completedEvent = JsonDocument.Parse(lines[1]);
        Assert.Equal("completed", completedEvent.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, completedEvent.RootElement.GetProperty("rowCount").GetInt64());
        Assert.Equal(-1, completedEvent.RootElement.GetProperty("recordsAffected").GetInt32());
    }

    [Fact]
    public async Task Ndjson_failure_before_streaming_returns_error_event_with_clean_status()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Early NDJSON boom", method = "GET", route = "ndjson-early-boom",
            connectionName = "Main", sql = "boom",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/pub/ndjson-early-boom");
        request.Headers.Accept.ParseAdd("application/x-ndjson");
        var invoke = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, invoke.StatusCode);
        Assert.Equal("application/x-ndjson", invoke.Content.Headers.ContentType!.MediaType);
        using var errorEvent = JsonDocument.Parse((await invoke.Content.ReadAsStringAsync()).Trim());
        Assert.Equal("error", errorEvent.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, errorEvent.RootElement.GetProperty("rowCount").GetInt64());
        Assert.Equal("kaboom", errorEvent.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ndjson_failure_after_rows_emits_terminal_error_event()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Midstream NDJSON boom", method = "GET", route = "ndjson-stream-boom",
            connectionName = "Main", sql = "stream-boom",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/pub/ndjson-stream-boom");
        request.Headers.Accept.ParseAdd("application/x-ndjson");
        var invoke = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        var lines = (await invoke.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var rowEvent = JsonDocument.Parse(lines[0]);
        Assert.Equal("row", rowEvent.RootElement.GetProperty("type").GetString());

        using var errorEvent = JsonDocument.Parse(lines[1]);
        Assert.Equal("error", errorEvent.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, errorEvent.RootElement.GetProperty("rowCount").GetInt64());
        Assert.Equal("mid-stream kaboom", errorEvent.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Unexpected_ndjson_failure_after_rows_is_sanitized()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        await Publish(client, new
        {
            name = "Unexpected stream", method = "GET", route = "unexpected-stream",
            connectionName = "Main", sql = "stream-unexpected-boom",
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gridlet/pub/unexpected-stream");
        request.Headers.Accept.ParseAdd("application/x-ndjson");
        var invoke = await client.SendAsync(request);
        var body = await invoke.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        Assert.Equal(2, lines.Length);
        using var errorEvent = JsonDocument.Parse(lines[1]);
        Assert.Contains(
            "unexpected server error",
            errorEvent.RootElement.GetProperty("error").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_PUBLISHED_SENTINEL", body, StringComparison.Ordinal);
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
        var deleted = await client.DeleteAsync($"/gridlet/api/published/{saved!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/pub/ephemeral")).StatusCode);
    }
}
