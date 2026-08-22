using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Components;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gridlet.Tests.Components;

public class ComponentEndpointTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(bool withComponents = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var gridlet = builder.Services.AddGridlet(options =>
        {
            options.Storage.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-tests-{Guid.NewGuid():n}.json");
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
        });

        if (withComponents)
        {
            gridlet.AddComponents(components => components.FilePath = Path.Combine(
                Path.GetTempPath(), $"gridlet-components-tests-{Guid.NewGuid():n}.json"));
        }

        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();

        var app = builder.Build();
        app.MapGridlet();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static object Definition(string layout = "free")
        => new { layout, width = 720, height = 460, css = "", controls = Array.Empty<object>() };

    [Fact]
    public async Task Components_roundtrip_through_the_store()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new { name = "Customer entry", schemaVersion = 1, definition = Definition() }))
            .Content.ReadFromJsonAsync<GridletComponent>();
        Assert.NotNull(saved);
        Assert.Equal("Customer entry", saved!.Name);

        var list = await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components");
        Assert.Single(list!);

        // Saving with the same id replaces rather than duplicating.
        await client.PostAsJsonAsync("/gridlet/api/components",
            new { id = saved.Id, name = "Customer entry", schemaVersion = 1, definition = Definition("grid") });
        list = await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components");
        Assert.Single(list!);
        Assert.Equal("grid", list![0].Definition.GetProperty("layout").GetString());

        var fetched = await client.GetFromJsonAsync<GridletComponent>($"/gridlet/api/components/{saved.Id}");
        Assert.Equal(saved.Id, fetched!.Id);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/gridlet/api/components/{saved.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<GridletComponent>>("/gridlet/api/components"))!);
    }

    [Fact]
    public async Task A_component_needs_a_name_and_an_object_definition()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var unnamed = await client.PostAsJsonAsync("/gridlet/api/components",
            new { name = "  ", schemaVersion = 1, definition = Definition() });
        Assert.Equal(HttpStatusCode.BadRequest, unnamed.StatusCode);

        var notAnObject = await client.PostAsJsonAsync("/gridlet/api/components",
            new { name = "Broken", schemaVersion = 1, definition = "not a document" });
        Assert.Equal(HttpStatusCode.BadRequest, notAnObject.StatusCode);
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
                schemaVersion = GridletComponent.CurrentSchemaVersion + 1,
                definition = Definition(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<GridletErrorResponse>();
        Assert.Contains("newer version", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definitions_are_stored_verbatim_including_properties_this_build_ignores()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        // A document is the operator's artifact. Round-tripping it must not quietly drop anything,
        // or an older build would silently strip a newer build's work on the next save.
        var saved = await (await client.PostAsJsonAsync("/gridlet/api/components",
                new
                {
                    name = "Rich",
                    schemaVersion = 1,
                    definition = new
                    {
                        layout = "free",
                        somethingUnknown = new { nested = true, list = new[] { 1, 2, 3 } },
                    },
                }))
            .Content.ReadFromJsonAsync<GridletComponent>();

        var reread = await client.GetFromJsonAsync<GridletComponent>($"/gridlet/api/components/{saved!.Id}");
        var unknown = reread!.Definition.GetProperty("somethingUnknown");
        Assert.True(unknown.GetProperty("nested").GetBoolean());
        Assert.Equal(3, unknown.GetProperty("list").GetArrayLength());
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
        Assert.Equal("designer.js", module.GetProperty("scripts")[0].GetString());

        var script = await client.GetAsync("/gridlet/assets/modules/components/designer.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("gridlet", await script.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

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
