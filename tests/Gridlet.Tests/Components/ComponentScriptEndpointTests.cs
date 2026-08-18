using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Components;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gridlet.Tests.Components;

/// <summary>
/// The JavaScript modules a component runs. They are files, served to the browser as real ES modules,
/// so what these check is that a module goes in and comes back out exactly as written and that a
/// name can only ever be a file in the module folder.
/// </summary>
public class ComponentScriptEndpointTests : IDisposable
{
    private readonly string _scripts = Path.Combine(
        Path.GetTempPath(), $"gridlet-components-scripts-{Guid.NewGuid():n}");

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
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

        gridlet.AddComponents(components =>
        {
            components.FilePath = Path.Combine(Path.GetTempPath(), $"gridlet-components-tests-{Guid.NewGuid():n}.json");
            components.ScriptsPath = _scripts;
        });

        builder.Services.AddSingleton<IGridletProvider, FakeGridletProvider>();

        var app = builder.Build();
        app.MapGridlet();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    public void Dispose()
    {
        if (Directory.Exists(_scripts))
        {
            Directory.Delete(_scripts, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private const string Source = """
        import { titleCase } from './text.js';

        export default class CustomerEntry {
          #component;

          constructor(component) {
            this.#component = component;
          }

          connected() {
            this.#component.on('row', (row) => {
              this.#component.field('name').value = titleCase(row.FirstName);
            });
          }
        }
        """;

    [Fact]
    public async Task Modules_roundtrip_as_files_and_are_stored_verbatim()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await (await client.PutAsJsonAsync(
                "/gridlet/api/components/scripts/customer-entry.js", new { source = Source }))
            .Content.ReadFromJsonAsync<GridletComponentScript>();

        Assert.Equal("customer-entry.js", saved!.Name);

        // Byte for byte: a module is source, and source that comes back changed is source nobody
        // can trust to a code review.
        Assert.Equal(Source, saved.Source);
        Assert.Equal(Source, await File.ReadAllTextAsync(Path.Combine(_scripts, "customer-entry.js")));

        // The list is everything a component can import, so it also carries the modules Gridlet ships;
        // the ones written here are the rest.
        var mine = (await client.GetFromJsonAsync<List<GridletComponentScript>>("/gridlet/api/components/scripts"))!
            .Where(script => !script.ReadOnly)
            .ToArray();
        Assert.Single(mine);
        Assert.Equal(Source, mine[0].Source);
        Assert.False(mine[0].ReadOnly);

        var fetched = await client.GetFromJsonAsync<GridletComponentScript>(
            "/gridlet/api/components/scripts/customer-entry.js");
        Assert.Equal(Source, fetched!.Source);

        Assert.Equal(HttpStatusCode.OK,
            (await client.DeleteAsync("/gridlet/api/components/scripts/customer-entry.js")).StatusCode);
        Assert.DoesNotContain(
            (await client.GetFromJsonAsync<List<GridletComponentScript>>("/gridlet/api/components/scripts"))!,
            script => !script.ReadOnly);
    }

    [Fact]
    public async Task A_module_is_served_as_javascript_the_browser_can_import()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        await client.PutAsJsonAsync("/gridlet/api/components/scripts/behaviour.js", new { source = Source });

        var response = await client.GetAsync("/gridlet/api/components/modules/17/behaviour.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Source, await response.Content.ReadAsStringAsync());

        // The version segment exists to get past the browser's module cache, so the answer behind
        // it must never be a cached one.
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task A_missing_module_fails_as_javascript_rather_than_as_a_page()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet/api/components/modules/1/nothing-here.js");

        // The caller is an import statement. HTML arriving where a module was expected is a syntax
        // error at a line nobody wrote, so the module says what went wrong in JavaScript.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("nothing-here.js", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../../secrets.js")]
    [InlineData("..%2F..%2Fsecrets.js")]
    [InlineData("nested/module.js")]
    [InlineData("module.txt")]
    [InlineData("module")]
    [InlineData(".hidden.js")]
    public async Task A_name_that_is_not_a_module_file_is_refused(string name)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var saved = await client.PutAsJsonAsync(
            $"/gridlet/api/components/scripts/{name}", new { source = "export default class X {}" });

        // Either the route never matches or the name is rejected; what must not happen is a file
        // being written somewhere a module cannot live.
        Assert.True(
            saved.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                or HttpStatusCode.MethodNotAllowed,
            $"'{name}' was accepted with {saved.StatusCode}");

        if (Directory.Exists(_scripts))
        {
            Assert.Empty(Directory.GetFiles(_scripts, "*", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public async Task Names_are_validated_the_same_way_everywhere()
    {
        Assert.True(GridletComponentScript.IsValidName("behaviour.js"));
        Assert.True(GridletComponentScript.IsValidName("customer-entry.v2.js"));
        Assert.False(GridletComponentScript.IsValidName("../escape.js"));
        Assert.False(GridletComponentScript.IsValidName("folder/module.js"));
        Assert.False(GridletComponentScript.IsValidName("module.mjs"));
        Assert.False(GridletComponentScript.IsValidName(".js"));
        Assert.False(GridletComponentScript.IsValidName(null));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gridlets_own_modules_are_listed_read_only_and_importable()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var listed = await client.GetFromJsonAsync<List<GridletComponentScript>>("/gridlet/api/components/scripts");
        var standard = Assert.Single(listed!, script => script.Name == "gridlet.js");
        Assert.True(standard.ReadOnly);

        // The functions an expression can call are declared in it rather than hidden in the
        // designer, so this is the file a component author reads to find out what json() does.
        Assert.Contains("export function json(", standard.Source, StringComparison.Ordinal);
        Assert.Contains("export const FUNCTIONS", standard.Source, StringComparison.Ordinal);

        // It is served through the same module route as your own, which is what lets a module
        // import it by name.
        var served = await client.GetAsync("/gridlet/api/components/modules/std/gridlet.js");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("text/javascript", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(standard.Source, await served.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Gridlets_own_modules_cannot_be_written_over_or_deleted()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        // Shadowing the name would change what every `import './gridlet.js'` already written means.
        var saved = await client.PutAsJsonAsync(
            "/gridlet/api/components/scripts/gridlet.js", new { source = "export const json = () => 'hijacked';" });
        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);

        var deleted = await client.DeleteAsync("/gridlet/api/components/scripts/gridlet.js");
        Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);

        var served = await client.GetAsync("/gridlet/api/components/modules/std/gridlet.js");
        Assert.DoesNotContain("hijacked", await served.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_modules_report_not_found()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/gridlet/api/components/scripts/absent.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync("/gridlet/api/components/scripts/absent.js")).StatusCode);
    }
}
