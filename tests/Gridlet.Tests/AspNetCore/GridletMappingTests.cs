using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class GridletMappingTests
{
    [Fact]
    public async Task Published_mapping_exposes_only_the_published_runtime()
    {
        var (app, client) = await StartAsync(MappingMode.Published, options =>
            options.Security.AllowAnonymous = true);
        await using var _ = app;

        var store = app.Services.GetRequiredService<IPublishedEndpointStore>();
        await store.SaveAsync(new PublishedEndpoint(
            "published-only", "Answer", "GET", "answer", "Main", "FakeDb", "SELECT 42",
            [], AuthorizationPolicy: null, Enabled: true, DateTimeOffset.UtcNow));

        var invoke = await client.GetAsync("/gridlet/pub/answer");

        Assert.Equal(HttpStatusCode.OK, invoke.StatusCode);
        using var body = JsonDocument.Parse(await invoke.Content.ReadAsStringAsync());
        Assert.Equal(42, body.RootElement.GetProperty("rows")[0].GetProperty("Answer").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/api/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/gridlet/assets/icon_sm.png")).StatusCode);
    }

    [Fact]
    public async Task Api_mapping_exposes_management_and_published_runtime_without_ui()
    {
        var (app, client) = await StartAsync(MappingMode.Api, options =>
            options.Security.AllowAnonymous = true);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/gridlet/api/meta")).StatusCode);
        var publish = await client.PostAsJsonAsync("/gridlet/api/published", new
        {
            name = "API-only answer", method = "GET", route = "api-only-answer",
            connectionName = "Main", database = "FakeDb", sql = "SELECT 42",
        });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/gridlet/pub/api-only-answer")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/gridlet/assets/icon_sm.png")).StatusCode);
    }

    [Fact]
    public async Task Published_mapping_requires_authorization_by_default()
    {
        var (app, _) = await StartAsync(MappingMode.Published);
        await using var appScope = app;

        var authorize = Assert.Single(GetPublishedEndpoint(app).Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(authorize.Policy);
    }

    [Fact]
    public async Task Published_mapping_named_policy_overrides_allow_anonymous()
    {
        var (app, _) = await StartAsync(MappingMode.Published, options =>
        {
            options.Security.AllowAnonymous = true;
            options.Security.AuthorizationPolicy = "PublishedConsumers";
        });
        await using var appScope = app;

        var authorize = Assert.Single(GetPublishedEndpoint(app).Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal("PublishedConsumers", authorize.Policy);
    }

    private static RouteEndpoint GetPublishedEndpoint(WebApplication app)
        => ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/gridlet/pub/{**route}");

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        MappingMode mode,
        Action<GridletOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddGridlet(options =>
        {
            options.Storage.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-mapping-tests-{Guid.NewGuid():n}.json");
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            configure?.Invoke(options);
        });
        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();

        var app = builder.Build();
        if (mode == MappingMode.Api)
        {
            app.MapGridletApi();
        }
        else
        {
            app.MapGridletPublished();
        }

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private enum MappingMode
    {
        Api,
        Published,
    }
}
