using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Components;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Components;

public class ComponentEndpointTests
{
    private sealed class NoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        bool withComponents = true,
        bool apiOnly = false,
        string? publishedApiRoutePrefix = null,
        bool allowAnonymous = true,
        string pattern = "/gridlet")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var gridlet = builder.Services.AddGridlet(options =>
        {
            options.Storage.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-tests-{Guid.NewGuid():n}.json");
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = allowAnonymous;
            if (publishedApiRoutePrefix is not null)
            {
                options.PublishedApiRoutePrefix = publishedApiRoutePrefix;
            }
        });

        if (withComponents)
        {
            gridlet.AddComponents(components => components.Path = Path.Combine(
                Path.GetTempPath(), $"gridlet-components-tests-{Guid.NewGuid():n}"));
        }

        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();
        if (!allowAnonymous)
        {
            builder.Services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>("Test", null);
            builder.Services.AddAuthorization();
        }

        var app = builder.Build();
        if (apiOnly)
        {
            app.MapGridletApi(pattern);
        }
        else
        {
            app.MapGridlet(pattern);
        }
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static string Document(string name = "Customer entry", string layout = "free")
        => $"""<div data-gridlet="2" data-name="{name}" data-layout="{layout}" style="width: 720px; height: 460px;"></div>""";

    [Fact]
    public async Task Components_roundtrip_through_the_store()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new { name = "Customer entry", html = Document() }))
            .Content.ReadFromJsonAsync<GridletComponent>();
        Assert.NotNull(saved);
        Assert.Equal("Customer entry", saved!.Name);

        var list = await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components");
        Assert.Single(list!);

        // Saving with the same id replaces rather than duplicating.
        await client.PostAsJsonAsync("/gridlet/api/components",
            new { id = saved.Id, name = "Customer entry", html = Document(layout: "grid") });
        list = await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components");
        Assert.Single(list!);
        Assert.Contains(@"data-layout=""grid""", list![0].Html, StringComparison.Ordinal);

        var fetched = await client.GetFromJsonAsync<GridletComponent>($"/gridlet/api/components/{saved.Id}");
        Assert.Equal(saved.Id, fetched!.Id);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/gridlet/api/components/{saved.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components"))!);
    }

    [Fact]
    public async Task A_saved_component_has_a_consumer_facing_runtime_page()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new { name = "Ignored request name", html = Document(name: "%RUNTIME_SCRIPT%") }))
            .Content.ReadFromJsonAsync<GridletComponent>();

        var response = await client.GetAsync($"/gridlet/components/{saved!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var policies));
        Assert.Contains("script-src 'self'", policies.Single(), StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", policies.Single(), StringComparison.Ordinal);
        var page = await response.Content.ReadAsStringAsync();

        Assert.Contains("<title>%RUNTIME_SCRIPT%</title>", page, StringComparison.Ordinal);
        Assert.Contains("gridlet-component-document", page, StringComparison.Ordinal);
        Assert.Contains("/gridlet/assets/modules/components/runtime.js", page, StringComparison.Ordinal);
        Assert.Contains("data-gridlet-published-segment=\"pub\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Components_reserve_the_components_published_route_segment()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(publishedApiRoutePrefix: "components"));
    }

    [Fact]
    public async Task A_component_runtime_page_uses_a_custom_gridlet_mount()
    {
        var (app, client) = await StartAsync(pattern: "/admin/tools");
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/admin/tools/api/components",
                new { name = "Custom mount", html = Document() }))
            .Content.ReadFromJsonAsync<GridletComponent>();

        var response = await client.GetAsync($"/admin/tools/components/{saved!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("/admin/tools/assets/modules/components/runtime.js", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_component_runtime_page_keeps_the_gridlet_authorization_boundary()
    {
        var (app, client) = await StartAsync(allowAnonymous: false);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/gridlet/components/any-component")).StatusCode);
    }

    [Fact]
    public async Task Api_only_mapping_does_not_advertise_a_runtime_page_without_runtime_assets()
    {
        var (app, client) = await StartAsync(apiOnly: true);
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new { name = "API-only component", html = Document() }))
            .Content.ReadFromJsonAsync<GridletComponent>();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/gridlet/components/{saved!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/gridlet/assets/modules/components/runtime.js")).StatusCode);
    }

    [Fact]
    public async Task A_component_needs_a_name_and_a_document_that_says_it_is_one()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var unnamed = await client.PostAsJsonAsync("/gridlet/api/components",
            new { name = "  ", html = Document() });
        Assert.Equal(HttpStatusCode.BadRequest, unnamed.StatusCode);

        // Markup with no version on it is either not a component or is damaged. Storing it would
        // leave a file the designer cannot open.
        var notADocument = await client.PostAsJsonAsync("/gridlet/api/components",
            new { name = "Broken", html = "<div><p>not a component</p></div>" });
        Assert.Equal(HttpStatusCode.BadRequest, notADocument.StatusCode);
    }

    [Fact]
    public async Task A_document_from_a_newer_build_is_refused_rather_than_downgraded()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/gridlet/api/components",
            new
            {
                name = "From the future",
                html = $"""<div data-gridlet="{GridletComponent.CurrentDocumentVersion + 1}" data-name="From the future"></div>""",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<GridletErrorResponse>();
        Assert.Contains("newer version", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Documents_are_stored_verbatim_including_markup_this_build_ignores()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        // A document is the operator's artifact and the server interprets none of it. Round-tripping
        // must not drop anything, or an older build would silently strip a newer build's work — or
        // somebody's own markup — on the next save.
        const string html = """
            <div data-gridlet="2" data-name="Rich" data-layout="free">
              <p>Some <b>bold</b> text &amp; an entity</p>
              <span data-name="label1" data-something-unknown="kept">Hello</span>
            </div>
            """;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new { name = "Rich", html }))
            .Content.ReadFromJsonAsync<GridletComponent>();

        var reread = await client.GetFromJsonAsync<GridletComponent>($"/gridlet/api/components/{saved!.Id}");
        Assert.Equal(html, reread!.Html);
    }

    [Fact]
    public async Task Missing_components_report_not_found()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/api/components/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/gridlet/api/components/nope")).StatusCode);
    }

    [Fact]
    public async Task Without_the_package_there_are_no_component_endpoints_and_no_module_in_meta()
    {
        var (app, client) = await StartAsync(withComponents: false);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/api/components")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/gridlet/components/nope")).StatusCode);

        var meta = await client.GetFromJsonAsync<JsonElement>("/gridlet/api/meta");
        var modules = meta.GetProperty("modules");
        Assert.Equal(0, modules.GetArrayLength());
    }

    [Fact]
    public async Task The_module_announces_and_serves_its_own_assets()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var meta = await client.GetFromJsonAsync<JsonElement>("/gridlet/api/meta");
        var module = meta.GetProperty("modules")[0];
        Assert.Equal("components", module.GetProperty("name").GetString());

        // Order matters: the designer reads and writes documents through the format module from the
        // moment it loads, so the format module has to be there first.
        Assert.Equal("format.js", module.GetProperty("scripts")[0].GetString());
        Assert.Equal("designer.js", module.GetProperty("scripts")[1].GetString());

        var format = await client.GetAsync("/gridlet/assets/modules/components/format.js");
        Assert.Equal(HttpStatusCode.OK, format.StatusCode);

        var script = await client.GetAsync("/gridlet/assets/modules/components/designer.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("gridlet", await script.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var runtime = await client.GetAsync("/gridlet/assets/modules/components/runtime.js");
        Assert.Equal(HttpStatusCode.OK, runtime.StatusCode);
        Assert.Contains("gridlet-component-runtime", await runtime.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var style = await client.GetAsync("/gridlet/assets/modules/components/designer.css");
        Assert.Equal(HttpStatusCode.OK, style.StatusCode);
    }

    // The traversal case is sent percent-encoded on purpose: unencoded "../.." is collapsed by the
    // client before it leaves, so only the encoded component actually reaches the module's own route.
    [Theory]
    [InlineData("%2E%2E%2F%2E%2E%2Fapp.js")]
    [InlineData("designer.js.map")]
    [InlineData("unknown.js")]
    public async Task A_module_serves_only_the_assets_it_declares(string path)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync($"/gridlet/assets/modules/components/{path}");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
