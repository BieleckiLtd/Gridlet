using System.Net;
using System.Net.Http.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Gridlet.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class GridletEndpointTests
{
    [Fact]
    public async Task Ui_index_is_served_with_mount_path_as_base_href()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/gridlet/\"", html);
        Assert.DoesNotContain("%GRIDLET_BASE%", html);
    }

    [Fact]
    public async Task Ui_respects_a_custom_mount_path()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            o =>
            {
                o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                o.Security.AllowAnonymous = true;
            },
            pattern: "/internal/db-admin");
        await using var _ = app;

        var html = await client.GetStringAsync("/internal/db-admin");

        Assert.Contains("<base href=\"/internal/db-admin/\"", html);
    }

    [Fact]
    public async Task Static_assets_are_served_from_embedded_resources()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var css = await client.GetAsync("/gridlet/assets/app.css");
        var js = await client.GetAsync("/gridlet/assets/app.js");
        var missing = await client.GetAsync("/gridlet/assets/nope.css");

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.StartsWith("text/css", css.Content.Headers.ContentType!.ToString());
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Meta_lists_connections_but_never_connection_strings()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("Main", body);
        Assert.Contains(FakeGridletProvider.Name.ToString(), body);
        Assert.DoesNotContain("secret-host", body);
        Assert.Contains("\"maxQueryResultRows\":10000", body);
        Assert.Contains("\"defaultSchema\":\"dbo\"", body);
        Assert.Contains("\"supportsStoredProcedures\":true", body);
        Assert.Contains("\"supportsTriggers\":true", body);
    }

    [Fact]
    public async Task Meta_exposes_the_developer_configured_query_safety_cap()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            o.Limits.MaxQueryResultRows = 12_345;
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"maxQueryResultRows\":12345", body);
    }

    [Fact]
    public async Task Meta_exposes_the_connection_default_database()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;Database=Reporting;", FakeGridletProvider.Name,
                connection => connection.DefaultDatabase = "Reporting");
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"defaultDatabase\":\"Reporting\"", body);
        Assert.DoesNotContain("Server=x", body);
    }

    [Fact]
    public async Task Databases_come_from_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/connections/Main/databases");

        Assert.Contains("FakeDb", body);
    }

    [Fact]
    public async Task Unknown_connection_returns_404_with_error_body()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet/api/connections/Nope/databases");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Objects_expose_type_names_as_strings()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/connections/Main/databases/FakeDb/objects");

        Assert.Contains("\"Table\"", body);
        Assert.Contains("\"View\"", body);
        Assert.Contains("\"Trigger\"", body);
    }

    [Fact]
    public async Task Data_endpoint_returns_a_page()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data?page=1&pageSize=50");

        Assert.Contains("totalRows", body);
    }

    [Fact]
    public async Task Data_stream_returns_progressive_events()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/stream?maxRows=100");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"type\":\"resultSet\"", body);
        Assert.Contains("\"type\":\"rows\"", body);
        Assert.Contains("\"type\":\"completed\"", body);
    }

    [Fact]
    public async Task Query_executes_and_returns_result_sets()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "SELECT 42" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"started\"", body);
        Assert.Contains("\"type\":\"rows\"", body);
        Assert.Contains("\"type\":\"completed\"", body);
        Assert.Contains("42", body);
        Assert.Contains("hello from fake", body);
    }

    [Fact]
    public async Task Failing_query_returns_400_with_database_error()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "boom" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kaboom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_row_cap_cannot_exceed_the_developer_configured_maximum()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            o.Limits.MaxQueryResultRows = 250;
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "SELECT 42", maxRows = 50_000 });

        var provider = Assert.IsType<FakeGridletProvider>(app.Services.GetRequiredService<IGridletProvider>());
        Assert.Equal(250, provider.LastQueryOptions!.MaxRowsPerResultSet);
    }

    [Fact]
    public async Task Query_is_forbidden_when_sql_execution_is_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Locked", "Server=x;", FakeGridletProvider.Name,
                c => c.AllowSqlExecution = false);
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Locked/databases/FakeDb/query",
            new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
