using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Gridlet.BrowserTests;

/// <summary>
/// The components designer, exercised through the browser that runs it.
/// </summary>
/// <remarks>
/// A component's formula language is evaluated in the page and drawn on a canvas, so what it does is
/// only really observable there: these tests read the control the operator would look at rather
/// than a value some seam hands back. Components are seeded through the same HTTP API the designer
/// itself calls, because building a layout by dragging would say nothing about the formulas being
/// tested and would take a hundred steps to arrive at the same document.
/// </remarks>
[Collection(BrowserCollection.Name)]
public sealed class GridletComponentsDesignerTests(BrowserAppFixture fixture)
{
    /// <summary>
    /// One control, as the markup a component document holds. This mirrors what the designer's own
    /// writer produces: a label is a span, a text box is an input, a button is a button. Building it
    /// here rather than posting a model keeps the tests honest about what is actually stored.
    /// </summary>
    private static string Control(
        string name,
        string type,
        object? bind = null,
        object? props = null,
        object? events = null,
        int x = 10,
        int y = 10,
        int w = 300,
        int h = 24,
        object? colors = null)
    {
        var properties = Values(props ?? new { text = "literal" });
        var attributes = new List<string>
        {
            $"data-name=\"{Escape(name)}\"",
            $"style=\"left: {x}px; top: {y}px; width: {w}px; height: {h}px;\"",
        };

        // colorLight / fillDark and so on, written as the panel writes them. A colour named for one
        // scheme only is the interesting case: the other scheme has to fall back to the same place
        // in Preview and on the public page.
        foreach (var (key, value) in Values(colors))
        {
            attributes.Add($"data-{Dashed(key)}=\"{Escape(value)}\"");
        }

        foreach (var (key, value) in Values(bind))
        {
            attributes.Add($"data-bind-{Dashed(key)}=\"{Escape(value)}\"");
        }

        foreach (var (key, value) in Values(events))
        {
            attributes.Add($"data-on-{Dashed(key)}=\"{Escape(value)}\"");
        }

        var text = properties.TryGetValue("text", out var t) ? Escape(t) : string.Empty;
        var attrs = string.Join(" ", attributes);

        return type switch
        {
            "label" => $"<span {attrs}>{text}</span>",
            "button" => $"<button type=\"button\" {attrs}>{text}</button>",
            "checkbox" => $"<label data-role=\"checkbox\" {attrs}><input type=\"checkbox\"><span>{text}</span></label>",
            "select" => $"<select {attrs}>{Options(properties)}</select>",
            "pager" => $"<div data-role=\"pager\" data-edges data-position {attrs}></div>",
            "grid" => $"<table data-role=\"grid\" {attrs}>{Header(properties)}</table>",
            "panel" => $"<div data-role=\"panel\" {attrs}></div>",
            "textarea" => $"<textarea data-role=\"textarea\" {Placeholder(properties)} {attrs}></textarea>",
            "textbox" when properties.TryGetValue("multiline", out var m) && m == "true"
                => $"<textarea {Placeholder(properties)} {attrs}></textarea>",
            _ => $"<input type=\"text\" {Placeholder(properties)} {attrs}>",
        };
    }

    private static string Options(IReadOnlyDictionary<string, string> properties)
        => properties.TryGetValue("options", out var options)
            ? string.Concat(options.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(option => $"<option>{Escape(option)}</option>"))
            : string.Empty;

    /// <summary>A grid's columns, which are its header - the rows are the source's, not the document's.</summary>
    private static string Header(IReadOnlyDictionary<string, string> properties)
        => properties.TryGetValue("columns", out var columns) && columns.Length > 0
            ? "<thead><tr>" + string.Concat(columns.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(column => $"<th>{Escape(column)}</th>")) + "</tr></thead>"
            : string.Empty;

    private static string Placeholder(IReadOnlyDictionary<string, string> properties)
        => properties.TryGetValue("placeholder", out var placeholder)
            ? $"placeholder=\"{Escape(placeholder)}\""
            : string.Empty;

    /// <summary>
    /// An anonymous object as the name/value pairs it stands for, so a test can keep writing
    /// <c>new { text = "=data.Name" }</c> while what gets stored is markup.
    /// </summary>
    private static Dictionary<string, string> Values(object? source)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is null)
        {
            return values;
        }

        foreach (var property in JsonSerializer.SerializeToElement(source).EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.ToString(),
            };
        }

        return values;
    }

    private static string Dashed(string name)
        => string.Concat(name.Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Seeds a component and opens it in the designer. Returns the page, ready on the canvas.
    /// </summary>
    private async Task<IPage> OpenComponentAsync(
        BrowserTestPage browserPage,
        string name,
        IEnumerable<string> controls,
        object? componentBind = null,
        object? componentEvents = null,
        IEnumerable<object>? modules = null,
        bool isolated = false,
        bool resizable = false,
        string css = "",
        string? source = null,
        string? route = null,
        object? colors = null,
        string box = "")
    {
        var page = browserPage.Page;

        // The component's own box, if it has one, beside the size every component has.
        var boxStyle = string.IsNullOrWhiteSpace(box) ? string.Empty : " " + box.Trim();

        var attributes = new List<string>
        {
            @"data-gridlet=""2""",
            $"data-name=\"{Escape(name)}\"",
            @"data-layout=""free""",
            $"style=\"width: 720px; height: 460px;{boxStyle}\"",
        };

        if (isolated)
        {
            attributes.Add("data-isolated");
        }

        if (resizable)
        {
            attributes.Add("data-resizable");
        }

        // The component's own colours. A control that names none must still reach its kind default
        // rather than inheriting these, which is the difference the two surfaces used to disagree on.
        foreach (var (key, value) in Values(colors))
        {
            attributes.Add($"data-{Dashed(key)}=\"{Escape(value)}\"");
        }

        foreach (var (key, value) in Values(componentBind))
        {
            attributes.Add($"data-bind-{Dashed(key)}=\"{Escape(value)}\"");
        }

        foreach (var (key, value) in Values(componentEvents))
        {
            attributes.Add($"data-on-{Dashed(key)}=\"{Escape(value)}\"");
        }

        var body = new List<string>();

        // The route the component reads its rows from. The document names the route rather than the
        // endpoint's id, so it keeps meaning the same endpoint in another environment.
        if (source is not null)
        {
            body.Add($"<gridlet-source href=\"{Escape(source)}\"></gridlet-source>");
        }

        foreach (var module in modules ?? [])
        {
            // The component's code-behind: the modules it runs, and optionally which of a module's
            // classes it runs with. A bare file name is the common case and stays a bare file name.
            if (module is string file)
            {
                body.Add($"<gridlet-code src=\"{Escape(file)}\"></gridlet-code>");
                continue;
            }

            var entry = Values(module);
            var src = entry.TryGetValue("module", out var moduleFile) ? moduleFile : string.Empty;
            var className = entry.TryGetValue("class", out var value) ? value : null;
            body.Add(className is null
                ? $"<gridlet-code src=\"{Escape(src)}\"></gridlet-code>"
                : $"<gridlet-code src=\"{Escape(src)}\" run=\"{Escape(className)}\"></gridlet-code>");
        }

        if (!string.IsNullOrEmpty(css))
        {
            body.Add($"<style>{css}</style>");
        }

        body.AddRange(controls);

        var html = $"<div {string.Join(" ", attributes)}>{string.Join("", body)}</div>";

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            // Existing consumer fixtures intentionally exercise a public route. New components
            // default to embedded-only; tests for that behavior opt out explicitly at the call site.
            DataObject = new { name, html, routable = true, route },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");

        await page.GotoAsync("/gridlet/");
        // Sidebar sections remember whether they were left open, and a fresh browser context has
        // nothing to remember, so Components starts closed.
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        // By the whole name rather than by containing it: the store is shared across the tests in
        // this collection, and one component's name can sit inside another's.
        await page.Locator($"button.tree-item[title^='{name} -']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();
        return page;
    }

    private static async Task WriteModuleAsync(IPage page, string name, string source)
    {
        var response = await page.APIRequest.PutAsync(
            $"/gridlet/api/components/scripts/{name}",
            new APIRequestContextOptions { DataObject = new { source } });
        Assert.True(response.Ok, $"Writing {name} failed: {response.Status}");
    }

    /// <summary>The control itself - the button, the input - which is what carries its name.</summary>
    private static ILocator Canvas(IPage page, string name) => page.Locator($"[data-name='{name}']");

    /// <summary>The box the designer positions the control with.</summary>
    private static ILocator Box(IPage page, string name) => page.Locator($"[data-control-box='{name}']");

    private static Task OpenPanelTabAsync(IPage page, string tab) =>
        page.Locator($".gfd-tabs button[title='{tab}']").ClickAsync();

    private static async Task PublishEndpointAsync(
        IPage page,
        string name,
        string method,
        string route,
        string sql,
        object[]? parameters = null)
    {
        var response = await page.APIRequest.PostAsync("/gridlet/api/published", new APIRequestContextOptions
        {
            DataObject = new
            {
                name,
                method,
                route,
                connectionName = "Main",
                sql,
                parameters = parameters ?? [],
                enabled = true,
            },
        });
        Assert.True(response.Ok, $"Publishing the endpoint failed: {response.Status}");
    }

    private static async Task<string> SaveComponentAsync(
        IPage page,
        string name,
        string html,
        bool routable = true,
        string? route = null,
        string? title = null)
    {
        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            // Existing consumer fixtures intentionally exercise a public route. New components
            // default to embedded-only; tests for that behavior opt out explicitly at the call site.
            DataObject = new { name, html, routable, route, title },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        return saved!.Value.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Component_sidebar_uses_a_compact_public_form_action_without_resolved_settings()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        var publicName = $"Public sidebar component {suffix}";
        var embeddedName = $"Embedded sidebar component {suffix}";
        var route = $"sidebar-public-{suffix}";
        var publicHtml = $"<div data-gridlet=\"2\" data-name=\"{publicName}\" data-layout=\"free\" style=\"width: 240px; height: 80px;\"></div>";
        var embeddedHtml = $"<div data-gridlet=\"2\" data-name=\"{embeddedName}\" data-layout=\"free\" style=\"width: 240px; height: 80px;\"></div>";
        await SaveComponentAsync(page, publicName, publicHtml, routable: true,
            route: route, title: "Public sidebar form");
        await SaveComponentAsync(page, embeddedName, embeddedHtml, routable: false);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();

        var publicItem = page.Locator("button.tree-item").Filter(
            new LocatorFilterOptions { HasTextString = publicName });
        var embeddedItem = page.Locator("button.tree-item").Filter(
            new LocatorFilterOptions { HasTextString = embeddedName });
        await Assertions.Expect(publicItem).ToBeVisibleAsync();
        await Assertions.Expect(embeddedItem).ToBeVisibleAsync();

        // Component rows no longer spend width on publication-state badges. Only a routable
        // component with a safe resolved URL gets the compact trailing action.
        await Assertions.Expect(publicItem.Locator(".badge")).ToHaveCountAsync(0);
        await Assertions.Expect(embeddedItem.Locator(".badge")).ToHaveCountAsync(0);
        await Assertions.Expect(publicItem.GetByTestId("component-public-open")).ToHaveCountAsync(1);
        await Assertions.Expect(embeddedItem.GetByTestId("component-public-open")).ToHaveCountAsync(0);

        // The trailing action must not bubble into the row's designer-opening handler.
        var publicForm = page.WaitForPopupAsync();
        await publicItem.GetByTestId("component-public-open").ClickAsync();
        var popup = await publicForm;
        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.EndsWith($"/gridlet/components/{route}", new Uri(popup.Url).AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("/gridlet/", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator(".gfd-designer")).ToHaveCountAsync(0);
        await popup.CloseAsync();

        // The same icon is keyboard-operable and still leaves the designer row untouched.
        var keyboardForm = page.WaitForPopupAsync();
        await publicItem.GetByTestId("component-public-open").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var keyboardPopup = await keyboardForm;
        await keyboardPopup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.EndsWith($"/gridlet/components/{route}", new Uri(keyboardPopup.Url).AbsolutePath, StringComparison.Ordinal);
        await keyboardPopup.CloseAsync();

        await publicItem.ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-designer")).ToBeVisibleAsync();
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.GetByTestId("component-routable")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("component-route")).ToHaveValueAsync(route);
        await Assertions.Expect(page.GetByTestId("component-title")).ToHaveValueAsync("Public sidebar form");
        await Assertions.Expect(page.GetByTestId("component-copy-public-url")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("Resolved URL", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("This component is available for embedding only.", new() { Exact = true }))
            .ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Preview_and_published_component_have_pixel_parity_at_the_same_viewport()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Pixel parity component {suffix}";
        var sourceRoute = $"pixel-parity-source-{suffix}";
        var componentRoute = $"pixel-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Pixel parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                Control("caption", "label", props: new { text = "Answer" }, x: 16, y: 16, w: 130, h: 30),
                Control("value", "textbox", bind: new { value = "=data.Answer" }, x: 160, y: 16, w: 280, h: 30),
                Control("choice", "checkbox", props: new { text = "Accept" }, x: 16, y: 54, w: 280, h: 30),
                Control("save", "button", props: new { text = "Save" }, x: 16, y: 96, w: 280, h: 34),
                Control("records", "grid", props: new { columns = "Answer", header = true }, x: 16, y: 146, w: 420, h: 160),
                Control("pager", "pager", x: 16, y: 320, w: 420, h: 34),
            ],
            source: sourceRoute,
            route: componentRoute);

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview [data-name='records'] tbody tr"))
            .ToHaveCountAsync(1);
        var preview = page.Locator(".gfd-canvas.preview");
        await BrowserPixelParity.StabilizeAsync(page, preview);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("#gridlet-component-host .gridlet-component-runtime"))
                .ToBeVisibleAsync();
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);
            var publishedRoot = published.Locator("#gridlet-component-host .gridlet-component-runtime");
            await BrowserPixelParity.StabilizeAsync(published, publishedRoot);

            var comparison = await BrowserPixelParity.CompareAsync(
                preview,
                publishedRoot,
                $"preview-published-{suffix}");
            Assert.True(comparison.IsMatch, comparison.ToString());
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Isolated_preview_and_published_component_have_pixel_parity()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Isolated parity component {suffix}";
        var sourceRoute = $"isolated-parity-source-{suffix}";
        var componentRoute = $"isolated-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Isolated parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                Control("caption", "label", props: new { text = "Answer" }, x: 16, y: 16, w: 130, h: 30),
                Control("value", "textbox", bind: new { value = "=data.Answer" }, x: 160, y: 16, w: 280, h: 30),
                Control("choice", "checkbox", props: new { text = "Accept" }, x: 16, y: 54, w: 280, h: 30),
                Control("records", "grid", props: new { columns = "Answer", header = true }, x: 16, y: 96, w: 420, h: 160),
                Control("pager", "pager", x: 16, y: 270, w: 420, h: 34),
            ],
            isolated: true,
            source: sourceRoute,
            route: componentRoute);

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview [data-name='records'] tbody tr"))
            .ToHaveCountAsync(1);
        var preview = page.Locator(".gfd-canvas.preview");
        await BrowserPixelParity.StabilizeAsync(page, preview);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("#gridlet-component-host .gridlet-component-runtime"))
                .ToBeVisibleAsync();
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);
            var publishedRoot = published.Locator("#gridlet-component-host .gridlet-component-runtime");
            await BrowserPixelParity.StabilizeAsync(published, publishedRoot);

            // Structure first, because it names the control that moved when something drifts.
            var structure = await BrowserPixelParity.CompareStructureAsync(preview, publishedRoot);
            Assert.True(structure.IsMatch, structure.ToString());

            // Then the pixels. Isolation hands the controls back to the browser, and the browser
            // draws them the same way in both documents, so this is a full comparison and not a
            // weaker stand-in for one.
            var pixels = await BrowserPixelParity.CompareAsync(
                preview,
                publishedRoot,
                $"isolated-preview-published-{suffix}");
            Assert.True(pixels.IsMatch, pixels.ToString());
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid is placed as a box and scrolls a table inside it. The published runtime moves the
    /// authored geometry onto that box, so a geometry binding has to move with it: writing the
    /// bound size to the table instead left the table sized independently of the box that clips it,
    /// which showed up as a second set of scrollbars and rows cut off part way down.
    /// </summary>
    [Fact]
    public async Task Published_grid_geometry_bindings_size_the_scrolling_viewport()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Grid geometry component {suffix}";
        var sourceRoute = $"grid-geometry-source-{suffix}";
        var componentRoute = $"grid-geometry-component-{suffix}";

        await PublishEndpointAsync(page, $"Grid geometry source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                Control(
                    "records",
                    "grid",
                    props: new { columns = "Answer", header = true },
                    bind: new
                    {
                        x = "=16",
                        w = "=component.width - 32 - self.x",
                        h = "=component.height - 60 - self.y",
                        y = "=120",
                    },
                    x: 16,
                    y: 120,
                    w: 672,
                    h: 280),
            ],
            source: sourceRoute,
            route: componentRoute);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);

            var geometry = await published.EvaluateAsync<JsonElement>("""
                () => {
                  const table = document.querySelector('[data-role="grid"]');
                  const viewport = table.parentElement;
                  return {
                    viewportClass: viewport.className,
                    left: viewport.style.left,
                    top: viewport.style.top,
                    width: viewport.style.width,
                    height: viewport.style.height,
                    tableWidth: table.style.width,
                    tableHeight: table.style.height,
                    scrollWidth: viewport.scrollWidth,
                    clientWidth: viewport.clientWidth,
                  };
                }
                """);

            // The bound box is the viewport's, worked out from the component's own size and the
            // grid's resolved position: 720 - 32 - 16 wide and 460 - 60 - 120 tall.
            Assert.Equal("gridlet-grid-viewport", geometry.GetProperty("viewportClass").GetString());
            Assert.Equal("16px", geometry.GetProperty("left").GetString());
            Assert.Equal("120px", geometry.GetProperty("top").GetString());
            Assert.Equal("672px", geometry.GetProperty("width").GetString());
            Assert.Equal("280px", geometry.GetProperty("height").GetString());

            // The table keeps sizing itself to its content inside that box, and the box therefore
            // never scrolls sideways over a table narrower than it is.
            Assert.Equal("max-content", geometry.GetProperty("tableWidth").GetString());
            Assert.Equal("auto", geometry.GetProperty("tableHeight").GetString());
            Assert.Equal(
                geometry.GetProperty("clientWidth").GetInt32(),
                geometry.GetProperty("scrollWidth").GetInt32());
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Preview resolves each bound property when something asks for it, so the order controls happen
    /// to be written in does not matter. The published runtime evaluated in document order instead,
    /// and a formula naming a field declared below it read that field before its own value binding
    /// had run.
    /// </summary>
    [Fact]
    public async Task A_published_binding_reads_a_control_declared_after_it()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Forward reference component {suffix}";
        var sourceRoute = $"forward-reference-source-{suffix}";
        var componentRoute = $"forward-reference-component-{suffix}";

        await PublishEndpointAsync(
            page,
            $"Forward reference source {suffix}",
            "GET",
            sourceRoute,
            "SELECT 42");

        await OpenComponentAsync(
            browserPage,
            name,
            [
                // The button is written above the field it names, which is the case that failed.
                Control(
                    "save",
                    "button",
                    bind: new { text = "=(\"Save \" + answer.value)" },
                    x: 16,
                    y: 16,
                    w: 280,
                    h: 34),
                Control("answer", "textbox", bind: new { value = "=data.Answer" }, x: 16, y: 60, w: 280, h: 30),
            ],
            source: sourceRoute,
            route: componentRoute);

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview [data-name='save']"))
            .ToHaveTextAsync("Save 42");

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='save']")).ToHaveTextAsync("Save 42");
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The colours a component does not name. Preview falls back to each control kind's default -
    /// a label's text colour, a field's fill - because the designer emits those defaults into the
    /// generated sheet. The published runtime had no such defaults and made an unnamed colour
    /// inherit the component's own instead, so a component with a pale text colour published every
    /// label, its pager and its grid heading in that colour. A colour named for one scheme only was
    /// dropped as well: the light rule carried four more :not()s than the dark one, so in dark mode
    /// it still won and read a variable nobody had set.
    /// </summary>
    [Fact]
    public async Task Preview_and_published_component_resolve_the_same_control_colours_in_dark_mode()
    {
        await using var browserPage = await fixture.NewVisualPageAsync(ColorScheme.Dark);
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Colour parity component {suffix}";
        var sourceRoute = $"colour-parity-source-{suffix}";
        var componentRoute = $"colour-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Colour parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                // Named for neither scheme: both surfaces have to reach the same kind default.
                Control("plain", "label", props: new { text = "Plain" }, x: 16, y: 16, w: 130, h: 30),
                Control("field", "textbox", bind: new { value = "=data.Answer" }, x: 160, y: 16, w: 280, h: 30),
                Control("notes", "textbox", props: new { multiline = "true" }, x: 160, y: 56, w: 280, h: 60),
                // Named for dark only: the light variable stays unset, and must not win.
                Control("tinted", "label", props: new { text = "Tinted" }, x: 16, y: 56, w: 130, h: 30,
                    colors: new { colorDark = "#9acd32" }),
                Control("choice", "checkbox", props: new { text = "Accept" }, x: 16, y: 130, w: 280, h: 30,
                    colors: new { fillDark = "#0a1ffc9e" }),
                Control("records", "grid", props: new { columns = "Answer", header = true }, x: 16, y: 170, w: 420, h: 140,
                    colors: new { colorDark = "#9acd32", fillDark = "#0e0e1ab1" }),
            ],
            source: sourceRoute,
            route: componentRoute,
            resizable: true,
            // The component's own text colour, deliberately nothing like a control default, so a
            // control that wrongly inherits it is unmistakable rather than accidentally right.
            colors: new { colorDark = "#83a1ff57" });

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview [data-name='records'] tbody tr"))
            .ToHaveCountAsync(1);
        var previewColours = await ControlColoursAsync(page, ".gfd-canvas.preview");

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);
            var publishedColours = await ControlColoursAsync(published, ".gridlet-component-runtime");

            Assert.Equal(previewColours, publishedColours);

            // The dark-only colours actually took, rather than both surfaces agreeing on nothing.
            Assert.Contains("rgb(154, 205, 50)", previewColours, StringComparison.Ordinal);
            Assert.Contains("rgba(10, 31, 252, 0.62)", previewColours, StringComparison.Ordinal);
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// What a reader can do to a component, rather than how it looks: a component the document marks
    /// resizable is resizable where it is read, a field is sized by the component rather than dragged
    /// by the reader, and a grid's columns can be widened. Preview had all three and the published
    /// page none of them.
    /// </summary>
    [Fact]
    public async Task A_published_component_resizes_and_its_grid_columns_can_be_dragged()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Resize parity component {suffix}";
        var sourceRoute = $"resize-parity-source-{suffix}";
        var componentRoute = $"resize-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Resize parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                Control("notes", "textbox", props: new { multiline = "true" }, x: 16, y: 16, w: 280, h: 60),
                Control("records", "grid", props: new { columns = "Answer", header = true }, x: 16, y: 96, w: 420, h: 140),
            ],
            source: sourceRoute,
            route: componentRoute,
            resizable: true);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);

            await Assertions.Expect(published.Locator(".gridlet-component-runtime"))
                .ToHaveCSSAsync("resize", "both");
            await Assertions.Expect(published.Locator("[data-name='notes']"))
                .ToHaveCSSAsync("resize", "none");

            var resize = await published.EvaluateAsync<JsonElement>("""
                () => {
                  const table = document.querySelector('[data-role="grid"]');
                  const cell = table.querySelector('thead th');
                  const grip = cell.querySelector('.col-grip');
                  const before = cell.offsetWidth;
                  const at = grip.getBoundingClientRect();
                  grip.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: at.left + 4, clientY: at.top + 4 }));
                  document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: at.left + 64, clientY: at.top + 4 }));
                  document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
                  return {
                    grips: table.querySelectorAll('thead .col-grip').length,
                    headings: [...table.querySelectorAll('thead th')].map((th) => th.textContent),
                    before,
                    after: cell.offsetWidth,
                  };
                }
                """);

            Assert.Equal(1, resize.GetProperty("grips").GetInt32());
            // The grip is a child of the heading cell, so it must not become part of the column name
            // the grid reads back out of its own header.
            Assert.Equal("Answer", resize.GetProperty("headings")[0].GetString());
            Assert.Equal(
                resize.GetProperty("before").GetInt32() + 60,
                resize.GetProperty("after").GetInt32());
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The resolved colours of the controls a colour-parity comparison cares about, in one string so
    /// a mismatch names the control and the property rather than failing on the first difference.
    /// </summary>
    private static async Task<string> ControlColoursAsync(IPage page, string root) =>
        await page.EvaluateAsync<string>("""
            (root) => {
              const surface = document.querySelector(root);
              const read = (name) => {
                const element = surface.querySelector(`[data-name="${name}"]`);
                const style = getComputedStyle(element);
                return `${name}: ${style.color} on ${style.backgroundColor}`;
              };
              return ['plain', 'field', 'notes', 'tinted', 'choice', 'records'].map(read).join('\n');
            }
            """, root);

    /// <summary>
    /// Anchoring is a formula, and a formula written against the component's size is only true of
    /// the size it was read at. Preview redraws its canvas whenever the box changes; the published
    /// page resolved its bindings once at load, so a reader who dragged a resizable component's
    /// corner got a bigger box with every control still sized for the old one.
    /// </summary>
    [Fact]
    public async Task A_published_component_re_anchors_its_controls_when_it_is_resized()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Anchor parity component {suffix}";
        var sourceRoute = $"anchor-parity-source-{suffix}";
        var componentRoute = $"anchor-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Anchor parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                // Stretched against the right edge, pinned to the bottom, and a field that reports
                // the width it was last resolved at.
                Control("stretch", "textbox", bind: new { x = "=16", w = "=component.width - 32 - self.x" },
                    x: 16, y: 16, w: 672, h: 30),
                Control("footer", "label", bind: new { y = "=component.height - 40" },
                    props: new { text = "Footer" }, x: 16, y: 420, w: 130, h: 30),
                Control("reported", "textbox", bind: new { value = "=component.width" },
                    x: 16, y: 56, w: 200, h: 30),
                Control("records", "grid", props: new { columns = "Answer", header = true },
                    bind: new { h = "=component.height - 200" }, x: 16, y: 96, w: 420, h: 260),
            ],
            source: sourceRoute,
            route: componentRoute,
            resizable: true);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);

            var layout = await published.EvaluateAsync<JsonElement>("""
                async () => {
                  const root = document.querySelector('.gridlet-component-runtime');
                  const read = () => ({
                    stretch: document.querySelector('[data-name="stretch"]').style.width,
                    footer: document.querySelector('[data-name="footer"]').style.top,
                    reported: document.querySelector('[data-name="reported"]').value,
                    grid: document.querySelector('.gridlet-grid-viewport').style.height,
                    rows: document.querySelectorAll('[data-name="records"] tbody tr').length,
                  });
                  const before = read();
                  root.style.width = '900px';
                  root.style.height = '600px';
                  await new Promise((done) => setTimeout(done, 300));
                  return { before, after: read() };
                }
                """);

            var before = layout.GetProperty("before");
            var after = layout.GetProperty("after");

            // The authored size the component was saved at.
            Assert.Equal("672px", before.GetProperty("stretch").GetString());
            Assert.Equal("420px", before.GetProperty("footer").GetString());
            Assert.Equal("720", before.GetProperty("reported").GetString());
            Assert.Equal("260px", before.GetProperty("grid").GetString());

            // 900 - 32 - 16 wide, pinned 40 up from 600, and the grid 200 short of it.
            Assert.Equal("852px", after.GetProperty("stretch").GetString());
            Assert.Equal("560px", after.GetProperty("footer").GetString());
            Assert.Equal("900", after.GetProperty("reported").GetString());
            Assert.Equal("400px", after.GetProperty("grid").GetString());

            // Laying out again must not rebuild rows the reader is looking at.
            Assert.Equal(before.GetProperty("rows").GetInt32(), after.GetProperty("rows").GetInt32());
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Two things an isolated grid keeps, because neither belongs to the primitives isolation hands
    /// back to the browser. Resizing a column is something the reader does, so the handle survives
    /// the reset - it lived in the chrome layer and an isolated public grid had no hit area at all.
    /// The scrollbars are the component's own frame, so they stay Gridlet's slim ones; and because
    /// `scrollbar-color` is inherited, stating them is also what stops a canvas taking the
    /// workspace's from the page around it and scrolling one way in Preview and another on its own.
    /// </summary>
    [Fact]
    public async Task An_isolated_grid_keeps_its_column_handles_and_the_browser_s_scrollbars()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Isolated grid component {suffix}";
        var sourceRoute = $"isolated-grid-source-{suffix}";
        var componentRoute = $"isolated-grid-component-{suffix}";

        await PublishEndpointAsync(page, $"Isolated grid source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [
                Control("records", "grid", props: new { columns = "Answer", header = true },
                    x: 16, y: 16, w: 300, h: 120),
            ],
            isolated: true,
            source: sourceRoute,
            route: componentRoute);

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview [data-name='records'] tbody tr"))
            .ToHaveCountAsync(1);
        var preview = await GridHandleStateAsync(page, ".gfd-canvas.preview", ".gfd-grid-viewport");

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='records'] tbody tr")).ToHaveCountAsync(1);
            var runtime = await GridHandleStateAsync(
                published, ".gridlet-component-runtime", ".gridlet-grid-viewport");

            Assert.Equal(preview, runtime);

            // And the handle is real on both, rather than both agreeing on an unusable one.
            Assert.Contains("grip absolute 8px", preview, StringComparison.Ordinal);
            Assert.Contains("scrollbar rgb(170, 180, 195) rgba(0, 0, 0, 0) thin", preview, StringComparison.Ordinal);
            Assert.Contains("resized true", preview, StringComparison.Ordinal);
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// How a grid's column handle is laid out, what its viewport asks of the scrollbar, and whether
    /// dragging the handle actually moves the column - as one string, so a mismatch says which.
    /// </summary>
    private static async Task<string> GridHandleStateAsync(IPage page, string root, string viewport) =>
        await page.EvaluateAsync<string>("""
            ([root, viewport]) => {
              const surface = document.querySelector(root);
              const view = surface.querySelector(viewport);
              const cell = surface.querySelector('thead th');
              const grip = cell.querySelector('.col-grip');
              const gripStyle = getComputedStyle(grip);
              const viewStyle = getComputedStyle(view);
              const before = cell.offsetWidth;
              const at = grip.getBoundingClientRect();
              grip.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: at.left + 4, clientY: at.top + 4 }));
              document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: at.left + 54, clientY: at.top + 4 }));
              document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
              return [
                `grip ${gripStyle.position} ${gripStyle.width} ${gripStyle.right}`,
                `cell ${getComputedStyle(cell).position}`,
                `scrollbar ${viewStyle.scrollbarColor} ${viewStyle.scrollbarWidth}`,
                `resized ${cell.offsetWidth === before + 50}`,
              ].join('; ');
            }
            """, new[] { root, viewport });

    /// <summary>
    /// The stylesheet every component is rendered with is a file in the Code section, beside
    /// Gridlet's own module, and opens in its own tab. Read-only, because it is part of the build:
    /// an edit would be lost on the next upgrade. It is shown as it was written rather than as the
    /// browser reparses it - `all: revert` comes back out of the CSSOM as every longhand it stands
    /// for, which is the same rule and unreadable.
    /// </summary>
    [Fact]
    public async Task The_code_section_lists_the_component_stylesheet_and_opens_it_read_only()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var listed = await page.APIRequest.GetAsync("/gridlet/api/components/scripts");
        Assert.True(listed.Ok, $"Listing the Code section failed: {listed.Status}");
        var files = await listed.JsonAsync();
        var stylesheet = files!.Value.EnumerateArray()
            .Single(file => file.GetProperty("name").GetString() == "component.css");
        Assert.True(stylesheet.GetProperty("readOnly").GetBoolean(), "the stylesheet must not be editable");

        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Code" }) });
        await section.Locator("summary").First.ClickAsync();

        var item = page.Locator("button.tree-item[title^='component.css']");
        await Assertions.Expect(item).ToBeVisibleAsync();
        // The section is called Code and mostly holds modules, but not everything in it is one, and
        // the list should say so at a glance rather than only in the file name.
        await Assertions.Expect(item.Locator(".badge")).ToHaveTextAsync("CSS");
        Assert.Contains("badge-stylesheet",
            await item.Locator(".badge").GetAttributeAsync("class") ?? string.Empty,
            StringComparison.Ordinal);
        // It is read, not imported, and the sidebar says which.
        Assert.Contains("the stylesheet every component is rendered with",
            await item.GetAttributeAsync("title") ?? string.Empty, StringComparison.Ordinal);

        await item.ClickAsync();
        var editor = page.GetByTestId("component-code-editor");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        Assert.True(await editor.EvaluateAsync<bool>("element => element.readOnly"));

        var source = await editor.InputValueAsync();
        Assert.Contains("@layer gridlet-reset, gridlet-chrome, gridlet;", source, StringComparison.Ordinal);
        Assert.Contains("all: revert;", source, StringComparison.Ordinal);
        // Both surfaces are named in it, which is what stops one rule meaning two different things.
        Assert.Contains(".gfd-canvas .gfd-control", source, StringComparison.Ordinal);
        Assert.Contains(".gridlet-component-runtime [data-name]", source, StringComparison.Ordinal);
        // The file as written, not a reparse: the CSSOM would have expanded the shorthand away.
        Assert.DoesNotContain("accent-color: revert", source, StringComparison.Ordinal);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A component's own box - its border, its corners, the space inside its edge and the space
    /// outside it. The designer paints the canvas with them through the generated sheet; the runtime
    /// reads them off the document's own style attribute. Two routes to the same rendering, so this
    /// checks they arrive at one.
    /// </summary>
    [Fact]
    public async Task Preview_and_published_component_render_the_same_box()
    {
        await using var browserPage = await fixture.NewVisualPageAsync(ColorScheme.Dark);
        var page = browserPage.Page;
        var suffix = Guid.NewGuid().ToString("n");
        var name = $"Box parity component {suffix}";
        var sourceRoute = $"box-parity-source-{suffix}";
        var componentRoute = $"box-parity-component-{suffix}";

        await PublishEndpointAsync(page, $"Box parity source {suffix}", "GET", sourceRoute, "SELECT 1");
        await OpenComponentAsync(
            browserPage,
            name,
            [Control("caption", "label", props: new { text = "Answer" }, x: 16, y: 16, w: 130, h: 30)],
            source: sourceRoute,
            route: componentRoute,
            // A border colour named for one scheme only, so the fallback is exercised too.
            colors: new { borderDark = "#ff9800" },
            box: "border-width: 3px; border-style: dashed; border-radius: 12px; padding: 10px; margin: 16px;");

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview")).ToBeVisibleAsync();
        var preview = await BoxOfAsync(page, ".gfd-canvas.preview");

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator(".gridlet-component-runtime")).ToBeVisibleAsync();
            var runtime = await BoxOfAsync(published, ".gridlet-component-runtime");

            Assert.Equal(preview, runtime);

            // And the values actually took, rather than both surfaces agreeing on nothing.
            Assert.Contains("border 3px dashed rgb(255, 152, 0)", preview, StringComparison.Ordinal);
            Assert.Contains("radius 12px", preview, StringComparison.Ordinal);
            Assert.Contains("padding 10px", preview, StringComparison.Ordinal);
            Assert.Contains("margin 16px", preview, StringComparison.Ordinal);
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>The component's own box, as one string, so a mismatch names the property.</summary>
    private static async Task<string> BoxOfAsync(IPage page, string root) =>
        await page.EvaluateAsync<string>("""
            (root) => {
              const style = getComputedStyle(document.querySelector(root));
              return [
                `border ${style.borderTopWidth} ${style.borderTopStyle} ${style.borderTopColor}`,
                `radius ${style.borderRadius}`,
                `padding ${style.padding}`,
                `margin ${style.margin}`,
              ].join('; ');
            }
            """, root);

    /// <summary>
    /// A component sized in per cent rather than pixels. `component.width` is then not a number until
    /// something has measured it, and Design used the authored value regardless - so `100%` was read
    /// as a hundred pixels and every anchored control was laid out against a component a fraction of
    /// its real width. Preview was right because it measured, which is why this only showed in Design.
    /// </summary>
    [Fact]
    public async Task Design_lays_out_anchored_controls_against_a_measured_percentage_width()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        // Named uniquely, because the store outlives one run and the sidebar is looked up by name.
        var page = await OpenComponentAsync(browserPage, $"Responsive component {Guid.NewGuid():n}",
            [
                Control("stretch", "textbox", bind: new { x = "=20", w = "=component.width - 40 - self.x" },
                    x: 20, y: 20, w: 300, h: 30),
            ],
            // A later declaration in the same attribute, which is how the panel writes a size the
            // document already carries.
            box: "width: 100%;");

        // A percentage is only a number once the canvas has been laid out, so the first render can
        // be a frame early and the size observer corrects it. Poll for the settled answer: what
        // matters is where the control ends up, not which frame it got there on.
        JsonElement layout = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            layout = await page.EvaluateAsync<JsonElement>("""
                () => {
                  const canvas = document.querySelector('.gfd-canvas');
                  const control = canvas?.querySelector('[data-name="stretch"]')?.closest('.gfd-control');
                  if (!canvas || !control) return { canvas: 0, control: 0, settled: false };
                  // A percentage canvas is a fractional width; the two boxes round independently.
                  const canvasWidth = canvas.getBoundingClientRect().width;
                  const controlWidth = control.getBoundingClientRect().width;
                  return {
                    canvas: Math.round(canvasWidth),
                    control: Math.round(controlWidth),
                    settled: Math.abs(controlWidth - (canvasWidth - 60)) <= 1,
                  };
                }
                """);
            if (layout.GetProperty("settled").GetBoolean()) break;
            await Task.Delay(100);
        }

        var canvas = layout.GetProperty("canvas").GetInt32();
        var control = layout.GetProperty("control").GetInt32();

        // The canvas really is being measured rather than collapsed, so the check below means
        // something: a hundred-pixel reading of "100%" would leave it far narrower than this.
        Assert.True(canvas > 400, $"the canvas measured {canvas}px, too narrow to tell the two readings apart");
        Assert.True(layout.GetProperty("settled").GetBoolean(),
            $"the control settled at {control}px against a {canvas}px canvas; expected {canvas - 60}px");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A component's edge is where the component ends: a control placed past it is clipped where the
    /// component is read, in Preview and on its published page alike.
    /// </summary>
    /// <remarks>
    /// Design deliberately does not clip. A resize handle sits five pixels outside the control it
    /// belongs to, so clipping the canvas would cut the handles off a control on the boundary and
    /// leave it impossible to drag back - which is a worse lie about the component than showing what
    /// is outside it. The clipping that matters is the clipping a reader sees, and this checks both
    /// places a reader sees one.
    /// </remarks>
    [Fact]
    public async Task A_control_beyond_the_edge_is_clipped_wherever_the_component_is_read()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var componentRoute = $"clipping-component-{suffix}";
        var page = await OpenComponentAsync(browserPage, $"Clipping component {suffix}",
            [
                Control("inside", "label", props: new { text = "Inside" }, x: 16, y: 16, w: 130, h: 30),
                // Placed well past the 720px edge the component was given.
                Control("outside", "button", props: new { text = "Outside" }, x: 900, y: 16, w: 160, h: 30),
            ],
            route: componentRoute);

        // Design: clipped, and clipped in a way that does not make the surface scroll to reach what
        // is outside the component.
        Assert.Equal("clip", await ClipOfAsync(page, ".gfd-canvas"));
        // The surface may scroll a little for the component's own margin, but it must not scroll out
        // to reach the control at x=900: that would be the component's overflow dragging the whole
        // surface along with it, which is how a stray control put a scrollbar under everything.
        var surfaceScrollWidth = await page.EvaluateAsync<int>(
            "() => document.querySelector('.gfd-surface').scrollWidth");
        Assert.True(surfaceScrollWidth < 900,
            $"the surface scrolled to {surfaceScrollWidth}px, far enough to reach a control outside the component");

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas.preview")).ToBeVisibleAsync();
        Assert.Equal("hidden", await ClipOfAsync(page, ".gfd-canvas.preview"));

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator(".gridlet-component-runtime")).ToBeVisibleAsync();
            Assert.Equal("hidden", await ClipOfAsync(published, ".gridlet-component-runtime"));
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// How a surface treats what is placed past its edge - and a check that there really is
    /// something out there, so "hidden" means something.
    /// </summary>
    private static async Task<string> ClipOfAsync(IPage page, string root) =>
        await page.EvaluateAsync<string>("""
            (root) => {
              const surface = document.querySelector(root);
              const outside = surface.querySelector('[data-name="outside"]');
              if (!outside) return 'the control is not there at all';
              const past = outside.getBoundingClientRect().right
                > surface.getBoundingClientRect().right + 1;
              if (!past) return 'nothing was placed past the edge';
              return getComputedStyle(surface).overflow;
            }
            """, root);

    /// <summary>
    /// A bound size that works out negative - `=component.width - 600` on a component narrower than
    /// that - is not a width a browser can use, and an invalid one falls back to `auto`. The control
    /// then sizes itself to its content and hangs past the edge it was measured against, which is
    /// the opposite of what the binding asked for. Nothing that wide is nothing wide.
    /// </summary>
    [Fact]
    public async Task A_bound_size_that_works_out_negative_is_no_size_at_all()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var componentRoute = $"negative-size-component-{suffix}";
        var page = await OpenComponentAsync(browserPage, $"Negative size component {suffix}",
            [
                Control("squeezed", "button", props: new { text = "Squeezed" },
                    bind: new { w = "=component.width - 900" }, x: 16, y: 16, w: 160, h: 30),
            ],
            route: componentRoute);

        var designWidth = await page.EvaluateAsync<int>("""
            () => Math.round(document.querySelector('.gfd-canvas [data-name="squeezed"]')
              .getBoundingClientRect().width)
            """);
        // A border-box cannot shrink below its own padding and border, so the floor is those rather
        // than nought. What matters is that it is the floor, and not the width the button would take
        // if it sized itself to the word inside it.
        Assert.InRange(designWidth, 0, 30);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator("[data-name='squeezed']")).ToBeAttachedAsync();
            var publishedWidth = await published.EvaluateAsync<int>("""
                () => Math.round(document.querySelector('[data-name="squeezed"]').getBoundingClientRect().width)
                """);
            Assert.InRange(publishedWidth, 0, 30);
            Assert.Equal(designWidth, publishedWidth);
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A component that fills what it is placed in and keeps a margin around itself fits inside it:
    /// no scrollbar under content that fits, wherever the component is shown. `100%` cannot say that
    /// on its own - a percentage resolves against the container and the margin is then added on top
    /// of it, so filling and keeping a margin would overflow by exactly the margin - so both
    /// surfaces apply the filling size in its place, and this checks the box that comes out.
    /// </summary>
    [Fact]
    public async Task A_component_that_fills_its_container_keeps_its_margin_inside_it()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var componentRoute = $"filling-component-{suffix}";
        var page = await OpenComponentAsync(browserPage, $"Filling component {suffix}",
            [Control("caption", "label", props: new { text = "Answer" }, x: 16, y: 16, w: 130, h: 30)],
            // Later declarations in the same attribute, which is how the panel writes them.
            box: "width: 100%; margin: 1em;",
            route: componentRoute);

        Assert.Equal(0, await FillGapAsync(page, ".gfd-surface", ".gfd-canvas"));

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator(".gridlet-component-runtime")).ToBeVisibleAsync();
            Assert.Equal(0, await FillGapAsync(published, "#gridlet-component-host", ".gridlet-component-runtime"));

            // And the page does not scroll under it either: a margin that escaped the host would
            // push the host down the page by exactly that margin.
            Assert.Equal(0, await published.EvaluateAsync<int>("""
                () => document.documentElement.scrollHeight - document.documentElement.clientHeight
                """));
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid wider than its columns has room to spare, and widening a column spends that room
    /// before it asks the reader for a scrollbar. The scrollbar is what says "there is more of this
    /// grid than you can see", so one under a grid that still fits inside its own box is a grid
    /// telling the reader something untrue about itself.
    /// </summary>
    [Fact]
    public async Task Widening_a_column_uses_the_room_a_grid_already_has_before_it_scrolls()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var componentRoute = $"column-room-component-{suffix}";
        await OpenComponentAsync(browserPage, $"Column room component {suffix}",
            [
                Control("records", "grid", props: new { columns = "One\nTwo", header = true },
                    x: 16, y: 16, w: 520, h: 200),
            ],
            route: componentRoute);

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator(".gridlet-grid-viewport")).ToBeVisibleAsync();

            var room = await GridRoomAsync(published);
            Assert.True(room.GetProperty("spare").GetInt32() > 60,
                $"the grid had no room to spend: {room}");
            Assert.Equal(0, room.GetProperty("scroll").GetInt32());

            // Widen the first column by less than the room the grid has.
            var grip = published.Locator(".gridlet-grid-viewport th").First.Locator(".col-grip");
            var box = await grip.BoundingBoxAsync() ?? throw new Xunit.Sdk.XunitException("no grip to drag");
            // The grip straddles the header cell's own clip edge, so its centre is outside what can
            // be clicked. Two pixels in from its left edge is on the half that is drawn.
            var x = box.X + 2;
            var y = box.Y + box.Height / 2;
            await published.Mouse.MoveAsync(x, y);
            await published.Mouse.DownAsync();
            await published.Mouse.MoveAsync(x + 40, y, new() { Steps = 4 });
            await published.Mouse.UpAsync();

            var widened = await GridRoomAsync(published);
            Assert.Equal(room.GetProperty("first").GetInt32() + 40, widened.GetProperty("first").GetInt32());
            Assert.Equal(0, widened.GetProperty("scroll").GetInt32());

            // And past the room it has, where a scrollbar is the truth.
            await published.EvaluateAsync("""
                () => {
                  // Every column at the widest a column is allowed to be, which no longer fits.
                  for (const cell of document.querySelectorAll('.gridlet-grid-viewport th')) {
                    cell.style.width = '420px';
                  }
                }
                """);
            Assert.True(await GridRoomAsync(published) is var overflowing
                && overflowing.GetProperty("scroll").GetInt32() > 0, "the grid did not scroll when its columns stopped fitting");
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// What a grid has room for: the width of its first column, the space its columns leave unused
    /// inside the viewport, and how far the viewport can be scrolled sideways.
    /// </summary>
    private static async Task<JsonElement> GridRoomAsync(IPage page) =>
        await page.EvaluateAsync<JsonElement>("""
            () => {
              const viewport = document.querySelector('.gridlet-grid-viewport');
              const cells = [...viewport.querySelectorAll('th')];
              const columns = cells.reduce((total, cell) => total + cell.offsetWidth, 0);
              const table = viewport.querySelector('[data-role="grid"]');
              return {
                first: cells[0].offsetWidth,
                spare: Math.round(viewport.clientWidth - columns),
                scroll: viewport.scrollWidth - viewport.clientWidth,
                asked: cells[0].style.width,
                layout: getComputedStyle(table).tableLayout,
                display: getComputedStyle(table).display,
                tableWidth: table.offsetWidth,
                viewportWidth: viewport.clientWidth,
              };
            }
            """);

    /// <summary>
    /// A component stops where it ends. Scrolling past the end of it does not bounce, glow, or
    /// carry on into the page around it - on the canvas, on the surface the canvas sits on, on the
    /// published page, and inside anything within a component that scrolls on its own, a grid being
    /// the one that scrolls most. What scrolling it does do settles rather than jumps.
    /// </summary>
    [Fact]
    public async Task Scrolling_past_the_end_of_a_component_does_not_carry_on_past_it()
    {
        await using var browserPage = await fixture.NewVisualPageAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var componentRoute = $"overscroll-component-{suffix}";
        var page = await OpenComponentAsync(browserPage, $"Overscroll component {suffix}",
            [
                Control("caption", "label", props: new { text = "Answer" }, x: 16, y: 16, w: 130, h: 30),
                Control("records", "grid", props: new { columns = "Answer", header = true },
                    x: 16, y: 66, w: 420, h: 160),
            ],
            route: componentRoute);

        // The surfaces that scroll, and a control that does not. The control matters: a control has
        // hidden overflow, which makes it a scroll container nobody can scroll, and telling one of
        // those not to chain stops a wheel over it from ever reaching the surface underneath.
        string[] canvasScrollers = [".gfd-surface", ".gfd-canvas", ".gfd-canvas .gfd-grid-viewport"];
        const string canvasCaption = ".gfd-canvas [data-name=\"caption\"]";


        // The tests run with motion turned down, which is where the smooth scrolling is meant to be
        // off: somebody who asked for less movement meant this movement too.
        Assert.Equal(["none/auto", "none/auto", "none/auto"], await ScrollStyleAsync(page, canvasScrollers));
        Assert.Equal(["auto/auto"], await ScrollStyleAsync(page, canvasCaption));
        

        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Equal(["none/smooth", "none/smooth", "none/smooth"], await ScrollStyleAsync(page, canvasScrollers));

        var published = await browserPage.Context.NewPageAsync();
        try
        {
            await published.GotoAsync($"/gridlet/components/{componentRoute}");
            await Assertions.Expect(published.Locator(".gridlet-component-runtime")).ToBeVisibleAsync();
            string[] pageScrollers =
            [
                "html", "body", "#gridlet-component-host",
                ".gridlet-component-runtime .gridlet-grid-viewport",
            ];

            Assert.Equal(["none/auto", "none/auto", "none/auto", "none/auto"],
                await ScrollStyleAsync(published, pageScrollers));
            Assert.Equal(["auto/auto"],
                await ScrollStyleAsync(published, ".gridlet-component-runtime [data-name=\"caption\"]"));

            await published.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            Assert.Equal(["none/smooth", "none/smooth", "none/smooth", "none/smooth"],
                await ScrollStyleAsync(published, pageScrollers));

            // And the grid scrolls under a wheel that is over its own rows, which is where a reader
            // puts the pointer to scroll them.
            var scrolled = await published.EvaluateAsync<JsonElement>("""
                () => {
                  const viewport = document.querySelector('.gridlet-grid-viewport');
                  const table = viewport.querySelector('[data-role="grid"]');
                  const body = table.tBodies[0] || table.createTBody();
                  for (let row = 0; row < 40; row += 1) {
                    body.insertRow(-1).insertCell(-1).textContent = `Row ${row}`;
                  }
                  const cell = body.rows[body.rows.length - 1].cells[0].getBoundingClientRect();
                  const over = viewport.getBoundingClientRect();
                  return {
                    scrollable: viewport.scrollHeight > viewport.clientHeight,
                    // A point over a row rather than over the empty part of the viewport.
                    x: Math.round(over.left + Math.min(over.width, cell.width) / 2),
                    y: Math.round(over.top + over.height / 2),
                  };
                }
                """);

            Assert.True(scrolled.GetProperty("scrollable").GetBoolean(), "the grid was not filled enough to scroll");
            await published.Mouse.MoveAsync(scrolled.GetProperty("x").GetInt32(), scrolled.GetProperty("y").GetInt32());
            await published.Mouse.WheelAsync(0, 200);
            await Assertions.Expect(published.Locator(".gridlet-grid-viewport")).Not.ToHaveJSPropertyAsync("scrollTop", 0);
        }
        finally
        {
            await published.CloseAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// What each of these elements does when a scroll reaches its end, and how it moves when it is
    /// scrolled by something other than a wheel - read as one string so a missing element is
    /// reported as the element it is rather than as a mismatched pair.
    /// </summary>
    private static async Task<string[]> ScrollStyleAsync(IPage page, params string[] selectors) =>
        await page.EvaluateAsync<string[]>("""
            (selectors) => selectors.map((selector) => {
              const element = document.querySelector(selector);
              if (!element) return `no ${selector}`;
              const style = getComputedStyle(element);
              return `${style.overscrollBehavior}/${style.scrollBehavior}`;
            })
            """, selectors);

    /// <summary>
    /// How far a filling component is from filling its container exactly: what is left over, plus
    /// anything it overflows by. Zero is the component sitting inside its container with its own
    /// margin around it and nothing to scroll to.
    /// </summary>
    private static async Task<int> FillGapAsync(IPage page, string container, string component)
    {
        var measured = await page.EvaluateAsync<JsonElement>("""
            ([container, component]) => {
              const outer = document.querySelector(container);
              const inner = document.querySelector(component);
              const margin = parseFloat(getComputedStyle(inner).marginLeft);
              return {
                margin: Math.round(margin),
                left: Math.round(outer.clientWidth - margin * 2 - inner.getBoundingClientRect().width),
                overflow: outer.scrollWidth - outer.clientWidth,
              };
            }
            """, new[] { container, component });

        Assert.True(measured.GetProperty("margin").GetInt32() > 0, "the component kept no margin to test");
        return Math.Abs(measured.GetProperty("left").GetInt32()) + measured.GetProperty("overflow").GetInt32();
    }

    [Fact]
    public async Task A_saved_component_can_be_given_to_a_consumer_without_the_designer()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var html = """
            <div data-gridlet="2" data-name="Consumer component" data-layout="free" data-isolated
                 data-color-light="#b42318" data-color-dark="#b42318"
                 data-fill-light="#e6f4ff" data-fill-dark="#e6f4ff"
                 data-bind-classes="consumer-bound" data-bind-element-id="consumer-root"
                 data-bind-tip="Consumer tip"
                 style="width: 360px; height: 120px;">
              <span data-name="caption" data-color-light="#067647" data-color-dark="#067647"
                    style="left: 16px; top: 20px; width: 250px; height: 30px;">Hello consumer</span>
              <input data-name="disabled-input" data-bind-enabled="false"
                     style="left: 16px; top: 50px; width: 250px; height: 30px;">
              <table data-role="grid" data-name="isolated-grid" data-no-header
                     data-color-light="#067647" data-color-dark="#067647"
                     data-fill-light="#fff1f2" data-fill-dark="#fff1f2"
                     style="left: 16px; top: 80px; width: 250px; height: 30px;"><thead><tr><th>Hidden</th></tr></thead></table>
            </div>
            """;

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Consumer component", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        var sidebarItem = page.Locator("button.tree-item[title^='Consumer component -']");
        await sidebarItem.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.Locator(".context-menu button").Filter(
            new LocatorFilterOptions { HasTextString = "Open consumer view" })).ToBeVisibleAsync();

        // A shared link often gains a trailing slash from a router or a copy/paste. The viewer must
        // resolve its runtime asset from the mount, not from the component id as a directory.
        await page.GotoAsync($"/gridlet/components/{id}/");
        await Assertions.Expect(page.Locator("#gridlet-component-host .gridlet-component-runtime"))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-name='caption']")).ToHaveTextAsync("Hello consumer");
        await Assertions.Expect(page.Locator("#gridlet-component-host .gridlet-component-runtime"))
            .ToHaveCSSAsync("width", "360px");
        var runtimeRoot = page.Locator("#consumer-root");
        Assert.Contains("consumer-bound", await runtimeRoot.GetAttributeAsync("class"));
        Assert.Equal("Consumer tip", await runtimeRoot.GetAttributeAsync("title"));
        await Assertions.Expect(runtimeRoot).ToHaveCSSAsync("color", "rgb(180, 35, 24)");
        await Assertions.Expect(runtimeRoot).ToHaveCSSAsync("background-color", "rgb(230, 244, 255)");
        await Assertions.Expect(page.Locator("[data-name='caption']"))
            .ToHaveCSSAsync("color", "rgb(6, 118, 71)");
        Assert.True(await page.Locator("[data-name='disabled-input']").IsDisabledAsync());
        await Assertions.Expect(page.Locator("[data-name='isolated-grid']"))
            .ToHaveCSSAsync("display", "block");
        // The scrolling box is the viewport the runtime wraps a grid in, exactly as the designer
        // places one, and the table inside it grows to its content. An isolated grid that scrolled
        // itself as well gave the published page a second set of scrollbars Preview never showed.
        await Assertions.Expect(page.Locator("[data-name='isolated-grid']"))
            .ToHaveCSSAsync("overflow", "visible");
        await Assertions.Expect(page.Locator(".gridlet-grid-viewport"))
            .ToHaveCSSAsync("overflow", "auto");
        await Assertions.Expect(page.Locator("[data-name='isolated-grid']"))
            .ToHaveCSSAsync("color", "rgb(6, 118, 71)");
        await Assertions.Expect(page.Locator("[data-name='isolated-grid']"))
            .ToHaveCSSAsync("background-color", "rgb(255, 241, 242)");
        await Assertions.Expect(page.Locator(".gfd-designer")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task A_consumer_component_keeps_authored_css_above_runtime_defaults()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var html = """
            <div data-gridlet="2" data-name="Styled consumer" data-layout="free" data-isolated
                 style="width: 360px; height: 100px;">
              <style>.btn { color: rgb(1, 2, 3); border: 7px solid rgb(4, 5, 6); }</style>
              <button data-name="action" class="btn" style="left: 16px; top: 20px; width: 250px; height: 30px;">Action</button>
            </div>
            """;

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Styled consumer", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync($"/gridlet/components/{id}");
        var action = page.Locator("[data-name='action']");
        await Assertions.Expect(action).ToHaveCSSAsync("color", "rgb(1, 2, 3)");
        await Assertions.Expect(action).ToHaveCSSAsync("border-top-width", "7px");
        await Assertions.Expect(action).ToHaveCSSAsync("border-top-color", "rgb(4, 5, 6)");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task A_consumer_runtime_supports_focus_handlers_and_chainable_component_events()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var moduleName = $"consumer-interactions-{Guid.NewGuid():n}.js";
        await WriteModuleAsync(page, moduleName, """
            export default class ConsumerInteractions {
              constructor(component) { this.component = component; }
              connected() { this.component.emit('ready').notify('Emit chain alive'); }
              focused() { this.component.notify('Focused'); }
              blurred() { this.component.notify('Blurred'); }
            }
            """);

        var html = """
            <div data-gridlet="2" data-name="Interaction consumer" data-layout="free"
                 style="width: 360px; height: 100px;">
              <gridlet-code src="MODULE"></gridlet-code>
              <label data-role="checkbox" data-name="choice"
                     data-on-focus="=focused()" data-on-blur="=blurred()"
                     style="left: 16px; top: 20px; width: 250px; height: 30px;">
                <input type="checkbox"><span>Choice</span>
              </label>
            </div>
            """.Replace("MODULE", moduleName, StringComparison.Ordinal);

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Interaction consumer", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync($"/gridlet/components/{id}");
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice"))
            .ToHaveTextAsync("Emit chain alive");

        var input = page.Locator("[data-name='choice'] input");
        await input.FocusAsync();
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice"))
            .ToHaveTextAsync("Focused");
        await input.BlurAsync();
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice"))
            .ToHaveTextAsync("Blurred");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task The_consumer_runtime_renders_saved_grids_pagers_panels_and_markup()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var route = $"consumer-controls-{Guid.NewGuid():n}";
        var published = await page.APIRequest.PostAsync("/gridlet/api/published", new APIRequestContextOptions
        {
            DataObject = new
            {
                name = "Consumer controls",
                method = "GET",
                route,
                connectionName = "Main",
                sql = "SELECT 1",
                parameters = Array.Empty<object>(),
                enabled = true,
            },
        });
        Assert.True(published.Ok, $"Publishing the endpoint failed: {published.Status}");

        var html = """
            <div data-gridlet="2" data-name="Consumer controls" data-layout="free"
                 style="width: 520px; height: 360px;">
              <gridlet-source href="ROUTE"></gridlet-source>
              <table data-role="grid" data-name="records" data-no-header
                     data-bind-columns="Answer" data-bind-header="false"
                     style="left: 16px; top: 16px; width: 300px; height: 100px;">
                <thead><tr><th>Answer</th></tr></thead>
              </table>
              <div data-role="pager" data-name="pager" data-edges data-position
                   data-bind-edges="false" data-bind-position="false"
                   style="left: 16px; top: 125px; width: 300px; height: 30px;"></div>
              <label data-role="checkbox" data-name="choice"
                     data-bind-value="true"
                     style="left: 16px; top: 165px; width: 300px; height: 30px;">
                <input type="checkbox"><span>Accept</span>
              </label>
              <input data-name="readonly-input" data-bind-read-only="true"
                     style="left: 16px; top: 195px; width: 300px; height: 30px;">
              <select data-name="options" data-bind-options="First&#10;Second"
                      style="left: 16px; top: 230px; width: 300px; height: 30px;"></select>
              <div data-role="panel" data-name="details"
                   data-bind-title="Bound details"
                   style="left: 16px; top: 205px; width: 300px; height: 90px;">
                <div data-role="panel-title">Details</div>
              </div>
              <span data-name="colored" data-bind-color.light="='#ff0000'" data-bind-color.dark="='#ff0000'"
                    style="left: 330px; top: 70px; width: 170px; height: 40px;">Colored</span>
              <gridlet-raw data-name="notes"
                           data-raw="&lt;p&gt;Hand-authored markup&lt;/p&gt;"
                           style="left: 330px; top: 16px; width: 170px; height: 40px;"></gridlet-raw>
            </div>
            """.Replace("ROUTE", route, StringComparison.Ordinal);

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Consumer controls", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync($"/gridlet/components/{id}");
        var grid = page.Locator("[data-name='records']");
        await Assertions.Expect(grid.Locator("thead")).ToHaveCountAsync(1);
        await Assertions.Expect(grid.Locator("thead")).ToHaveCSSAsync("display", "none");
        await Assertions.Expect(grid.Locator("thead th")).ToHaveTextAsync("Answer");
        await Assertions.Expect(grid.Locator("tbody td")).ToHaveTextAsync("42");
        await Assertions.Expect(page.Locator("[data-name='pager'] button[title='Next record']"))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-name='pager'] .gridlet-pager-position")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-name='pager'] button[title='First record']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-name='choice']")).ToContainTextAsync("Accept");
        Assert.True(await page.Locator("[data-name='choice'] input").IsCheckedAsync());
        Assert.False(await page.Locator("[data-name='readonly-input']").IsEditableAsync());
        await Assertions.Expect(page.Locator("[data-name='options'] option")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("[data-name='details']"))
            .ToHaveCSSAsync("border-radius", "6px");
        await Assertions.Expect(page.Locator("[data-role='panel-title']")).ToHaveTextAsync("Bound details");
        await Assertions.Expect(page.Locator("[data-name='colored']"))
            .ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(page.Locator("[data-name='notes']"))
            .ToContainTextAsync("Hand-authored markup");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task The_consumer_runtime_reads_a_published_record_and_keeps_document_markup_inert()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var route = $"consumer-record-{Guid.NewGuid():n}";
        var published = await page.APIRequest.PostAsync("/gridlet/api/published", new APIRequestContextOptions
        {
            DataObject = new
            {
                name = "Consumer record",
                method = "GET",
                route,
                connectionName = "Main",
                sql = "SELECT 1",
                parameters = Array.Empty<object>(),
                enabled = true,
            },
        });
        Assert.True(published.Ok, $"Publishing the endpoint failed: {published.Status}");

        var moduleName = $"consumer-values-{Guid.NewGuid():n}.js";
        await WriteModuleAsync(page, moduleName, """
            export const VatRate = 0.2;
            export default class ConsumerBehaviour {
              #services;
              constructor(component, services) { this.#services = services; }
              connected() { this.#services.notify('Consumer ready'); }
              rate() {
                this.#services.storage.write('rate', VatRate);
                return this.#services.storage.read('rate');
              }
            }
            """);

        var html = """
            <div data-gridlet="2" data-name="Bound consumer" data-layout="free"
                 style="width: 360px; height: 120px;">
              <gridlet-source href="ROUTE"></gridlet-source>
              <gridlet-code src="MODULE"></gridlet-code>
              <span data-name="answer" data-bind-text="=data.Answer"
                    style="left: 16px; top: 20px; width: 250px; height: 30px;">waiting</span>
              <span data-name="vat" data-bind-text="=VatRate"
                    style="left: 16px; top: 50px; width: 250px; height: 30px;">waiting</span>
              <span data-name="rate" data-bind-text="=rate()"
                    style="left: 16px; top: 80px; width: 250px; height: 30px;">waiting</span>
              <span data-name="qualified-rate" data-bind-text="=ConsumerBehaviour.rate()"
                    style="left: 16px; top: 110px; width: 250px; height: 30px;">waiting</span>
              <span data-name="gridlet-function" data-bind-text="=gridlet.upper('ok')"
                    style="left: 16px; top: 140px; width: 250px; height: 30px;">waiting</span>
              <form data-name="unsafe-form" data-bind-action="https://example.invalid/collect"
                    style="left: 16px; top: 80px; width: 250px; height: 30px;"></form>
              <script>window.__gridletDocumentScriptRan = true;</script>
              <meta http-equiv="refresh" content="0;url=https://example.invalid/">
              <a data-name="unsafe-link" href="javascript:window.__gridletDocumentScriptRan = true">link</a>
            </div>
            """.Replace("ROUTE", route, StringComparison.Ordinal)
            .Replace("MODULE", moduleName, StringComparison.Ordinal);

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Bound consumer", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync($"/gridlet/components/{id}");
        await Assertions.Expect(page.Locator("[data-name='answer']")).ToHaveTextAsync("42");
        await Assertions.Expect(page.Locator("[data-name='vat']")).ToHaveTextAsync("0.2");
        await Assertions.Expect(page.Locator("[data-name='rate']")).ToHaveTextAsync("0.2");
        await Assertions.Expect(page.Locator("[data-name='qualified-rate']")).ToHaveTextAsync("0.2");
        await Assertions.Expect(page.Locator("[data-name='gridlet-function']")).ToHaveTextAsync("OK");
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice")).ToHaveTextAsync("Consumer ready");
        await Assertions.Expect(page.Locator("script")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#gridlet-component-host meta")).ToHaveCountAsync(0);
        Assert.Null(await page.Locator("[data-name='unsafe-link']").GetAttributeAsync("href"));
        Assert.Null(await page.Locator("[data-name='unsafe-form']").GetAttributeAsync("action"));
        Assert.False(await page.EvaluateAsync<bool>("() => Boolean(window.__gridletDocumentScriptRan)"));

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task A_consumer_source_error_does_not_stop_its_behaviour_modules()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var moduleName = $"consumer-source-error-{Guid.NewGuid():n}.js";
        await WriteModuleAsync(page, moduleName, """
            export default class SourceErrorBehaviour {
              constructor(component) { this.component = component; }
              connected() { this.component.notify('Behavior alive'); }
            }
            """);

        var html = """
            <div data-gridlet="2" data-name="Source error" data-layout="free"
                 style="width: 360px; height: 120px;">
              <gridlet-source href="missing-consumer-source"></gridlet-source>
              <gridlet-code src="MODULE"></gridlet-code>
              <span data-name="value" style="left: 16px; top: 20px; width: 250px; height: 30px;">waiting</span>
            </div>
            """.Replace("MODULE", moduleName, StringComparison.Ordinal);

        var response = await page.APIRequest.PostAsync("/gridlet/api/components", new APIRequestContextOptions
        {
            DataObject = new { name = "Source error", html, routable = true },
        });
        Assert.True(response.Ok, $"Seeding the component failed: {response.Status}");
        var saved = await response.JsonAsync();
        var id = saved!.Value.GetProperty("id").GetString();

        await page.GotoAsync($"/gridlet/components/{id}");
        await Assertions.Expect(page.Locator(".gridlet-runtime-message"))
            .ToContainTextAsync("returned 404");
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice"))
            .ToHaveTextAsync("Behavior alive");
        await Assertions.Expect(page.Locator("[data-name='value']")).ToHaveTextAsync("waiting");

        browserPage.AssertNoUnexpectedErrors("Failed to load resource");
    }

    [Fact]
    public async Task A_consumer_form_uses_distinct_published_methods_and_current_values()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        var getRoute = $"form-read-{suffix}";
        var addRoute = $"form-add-{suffix}";
        var updateRoute = $"form-update-{suffix}";
        var deleteRoute = $"form-delete-{suffix}";
        var parameters = new object[]
        {
            new { name = "Name", required = true, type = "string" },
            new { name = "Enabled", required = true, type = "boolean" },
            new { name = "Count", required = true, type = "integer" },
            new { name = "Empty", required = false, type = "string" },
        };

        await PublishEndpointAsync(page, "Form read", "GET", getRoute, "SELECT 42");
        await PublishEndpointAsync(page, "Form add", "POST", addRoute, "ADD", parameters);
        await PublishEndpointAsync(page, "Form update", "PUT", updateRoute, "UPDATE", parameters[..1]);
        await PublishEndpointAsync(page, "Form delete", "DELETE", deleteRoute, "DELETE", parameters[..1]);

        var html = $"""
            <div data-gridlet="2" data-name="Published form" data-layout="free" style="width: 520px; height: 220px;">
              <gridlet-source href="/{getRoute}/"></gridlet-source>
              <gridlet-action name="add" method="POST" href="/{addRoute}/">
                <param name="Name" control="name">
                <param name="Enabled" control="enabled">
                <param name="Count" control="count">
                <param name="Empty" null>
              </gridlet-action>
              <gridlet-action name="update" method="PUT" href="/{updateRoute}/">
                <param name="Name" control="name">
              </gridlet-action>
              <gridlet-action name="delete" method="DELETE" href="/{deleteRoute}/">
                <param name="Name" control="name">
              </gridlet-action>
              <input data-name="name" value="before" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <input type="checkbox" data-name="enabled" style="left: 16px; top: 52px; width: 220px; height: 30px;">
              <input data-name="count" value="7" style="left: 16px; top: 88px; width: 220px; height: 30px;">
              <button type="button" data-name="add" data-action="add" style="left: 260px; top: 16px; width: 110px; height: 30px;">Add</button>
              <button type="button" data-name="update" data-action="update" style="left: 260px; top: 52px; width: 110px; height: 30px;">Update</button>
              <button type="button" data-name="delete" data-action="delete" style="left: 260px; top: 88px; width: 110px; height: 30px;">Delete</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Published form", html);

        var publishedRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/gridlet/pub/", StringComparison.Ordinal))
            {
                publishedRequests.Add(request);
            }
        };
        var sourceResponse = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET" && response.Url.EndsWith('/' + getRoute, StringComparison.Ordinal));
        await page.GotoAsync($"/gridlet/components/{id}");
        await sourceResponse;

        // The source is a read-only GET. Its request is separate from every action and carries no
        // write body, so merely loading the form cannot select an insert/update/delete endpoint.
        var sourceRequest = Assert.Single(publishedRequests, request => request.Url.EndsWith('/' + getRoute, StringComparison.Ordinal));
        Assert.Equal("GET", sourceRequest.Method);
        Assert.Null(sourceRequest.PostData);
        Assert.Equal("SELECT 42", fixture.Provider.LastQuerySql);

        await page.Locator("[data-name='name']").FillAsync("edited");
        await page.Locator("[data-name='enabled']").CheckAsync();
        await page.Locator("[data-name='enabled']").UncheckAsync();
        await page.Locator("[data-name='count']").FillAsync("0");

        var addRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + addRoute, StringComparison.Ordinal));
        await page.Locator("[data-name='add']").ClickAsync();
        var addRequest = await addRequestTask;
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        using (var body = JsonDocument.Parse(addRequest.PostData!))
        {
            Assert.Equal("edited", body.RootElement.GetProperty("Name").GetString());
            Assert.False(body.RootElement.GetProperty("Enabled").GetBoolean());
            Assert.Equal("0", body.RootElement.GetProperty("Count").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("Empty").ValueKind);
        }
        Assert.Equal("ADD", fixture.Provider.LastQuerySql);
        Assert.Equal(0L, fixture.Provider.LastQueryParameters!["Count"]);
        Assert.False((bool)fixture.Provider.LastQueryParameters["Enabled"]!);
        Assert.Null(fixture.Provider.LastQueryParameters["Empty"]);

        var updateRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "PUT" && request.Url.EndsWith('/' + updateRoute, StringComparison.Ordinal));
        await page.Locator("[data-name='update']").ClickAsync();
        var updateRequest = await updateRequestTask;
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("update completed successfully.");
        Assert.Equal("PUT", updateRequest.Method);
        Assert.Contains('/' + updateRoute, updateRequest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain('/' + addRoute, updateRequest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain('/' + getRoute, updateRequest.Url, StringComparison.Ordinal);

        var deleteRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "DELETE" && request.Url.EndsWith('/' + deleteRoute, StringComparison.Ordinal));
        await page.Locator("[data-name='delete']").ClickAsync();
        var deleteRequest = await deleteRequestTask;
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("delete completed successfully.");
        Assert.Equal("DELETE", deleteRequest.Method);
        Assert.Contains('/' + deleteRoute, deleteRequest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain('/' + updateRoute, deleteRequest.Url, StringComparison.Ordinal);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task A_form_fails_closed_for_mismatched_or_unsafe_actions()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        var addRoute = $"form-guard-add-{suffix}";
        await PublishEndpointAsync(page, "Guard add", "POST", addRoute, "GUARD");
        var providerSqlBeforeGuard = fixture.Provider.LastQuerySql;
        var publishedRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/gridlet/pub/", StringComparison.Ordinal)) publishedRequests.Add(request);
        };

        // A mismatched declaration remains parseable, but the runtime rejects it before fetch.
        var mismatchedHtml = $"""
            <div data-gridlet="2" data-name="Mismatched form" data-layout="free" style="width: 420px; height: 100px;">
              <gridlet-action name="update" method="POST" href="{addRoute}"></gridlet-action>
              <input data-name="name" value="value" style="left: 16px; top: 16px; width: 200px; height: 30px;">
              <button type="button" data-name="wrong" data-action="update" style="left: 230px; top: 16px; width: 80px; height: 30px;">Wrong</button>
            </div>
            """;
        var mismatchedId = await SaveComponentAsync(page, "Mismatched form", mismatchedHtml);
        await page.GotoAsync($"/gridlet/components/{mismatchedId}");

        await page.Locator("[data-name='wrong']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToContainTextAsync("update failed:");
        Assert.DoesNotContain("successfully", await page.Locator(".gridlet-action-status").TextContentAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(await page.Locator("[data-name='wrong']").IsDisabledAsync());
        Assert.Empty(publishedRequests);

        // An unsafe traversal declaration is rejected while parsing the saved consumer document.
        var unsafeHtml = """
            <div data-gridlet="2" data-name="Unsafe form" data-layout="free" style="width: 420px; height: 100px;">
              <gridlet-action name="delete" method="DELETE" href="../api/components"></gridlet-action>
              <button type="button" data-name="unsafe" data-action="delete" style="left: 16px; top: 16px; width: 80px; height: 30px;">Unsafe</button>
            </div>
            """;
        var unsafeId = await SaveComponentAsync(page, "Unsafe form", unsafeHtml);
        await page.GotoAsync($"/gridlet/components/{unsafeId}");
        await Assertions.Expect(page.Locator(".gridlet-runtime-message"))
            .ToContainTextAsync("published route is unsafe or malformed");
        Assert.Empty(publishedRequests);
        Assert.Equal(providerSqlBeforeGuard, fixture.Provider.LastQuerySql);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task A_form_error_reenables_the_button_without_reporting_success()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"form-error-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Form error", "POST", route, "stream-boom");
        var html = $"""
            <div data-gridlet="2" data-name="Error form" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-action name="add" method="POST" href="{route}"></gridlet-action>
              <button type="button" data-name="submit" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Submit</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Error form", html);
        await page.GotoAsync($"/gridlet/components/{id}");

        await page.Locator("[data-name='submit']").ClickAsync();
        var status = page.Locator(".gridlet-action-status");
        await Assertions.Expect(status).ToContainTextAsync("add failed: mid-stream kaboom");
        Assert.DoesNotContain("successfully", await status.TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await page.Locator("[data-name='submit']").IsDisabledAsync());
        Assert.Equal("stream-boom", fixture.Provider.LastQuerySql);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_round_trips_action_endpoint_mappings_and_button_binding()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var route = $"form-roundtrip-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Round-trip add", "POST", route, "ROUNDTRIP", [
            new { name = "Value", required = true, type = "string" },
            new { name = "Optional", required = false, type = "string" },
        ]);
        var html = """
            <div data-gridlet="2" data-name="Round-trip form" data-layout="free" style="width: 420px; height: 130px;">
              <input data-name="value" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Round-trip form", html);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Round-trip form -']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToBeVisibleAsync();

        await page.GetByTestId("component-action-add").SelectOptionAsync(route);
        await Assertions.Expect(page.GetByTestId("component-action-add-map-Value")).ToBeVisibleAsync();
        await page.GetByTestId("component-action-add-map-Value").SelectOptionAsync("control:value");

        await Box(page, "send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("control-action")).ToBeVisibleAsync();
        await page.GetByTestId("control-action").SelectOptionAsync("add");
        await page.GetByTestId("component-save").ClickAsync();

        // Preview runs the same explicit action declaration while keeping the value the operator
        // just entered, so the designer and consumer submit paths cannot drift apart.
        await page.GetByTestId("component-view-preview").ClickAsync();
        await page.Locator("[data-name='value']").FillAsync("sent from preview");
        var previewRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
        await page.Locator("[data-name='send']").ClickAsync();
        var previewRequest = await previewRequestTask;
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        using (var previewBody = JsonDocument.Parse(previewRequest.PostData!))
        {
            Assert.Equal("sent from preview", previewBody.RootElement.GetProperty("Value").GetString());
        }

        var saved = await page.APIRequest.GetAsync($"/gridlet/api/components/{id}");
        Assert.True(saved.Ok, $"Reading the saved component failed: {saved.Status}");
        var stored = await saved.JsonAsync();
        var storedHtml = stored!.Value.GetProperty("html").GetString()!;
        Assert.Contains($"<gridlet-action name=\"add\" method=\"POST\" href=\"{route}\">", storedHtml,
            StringComparison.Ordinal);
        Assert.Contains("<param name=\"Value\" control=\"value\">", storedHtml, StringComparison.Ordinal);
        Assert.Contains("data-action=\"add\"", storedHtml, StringComparison.Ordinal);

        // Read it back through the designer parser, not only through the saved string: both selectors
        // should still show the declared operation after a fresh document round-trip.
        await page.Locator(".tab.active .tab-close").ClickAsync();
        await page.Locator("button.tree-item[title^='Round-trip form -']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(route);
        await Box(page, "send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("control-action")).ToHaveValueAsync("add");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_surfaces_a_stale_action_mapping_after_the_control_is_renamed_or_deleted()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var route = $"stale-mapping-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Stale mapping add", "POST", route, "STALE MAPPING", [
            new { name = "Value", required = true, type = "string" },
        ]);
        var html = $"""
            <div data-gridlet="2" data-name="Stale mapping" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}">
                <param name="Value" control="value">
              </gridlet-action>
              <input data-name="value" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="add" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        await SaveComponentAsync(page, "Stale mapping", html);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Stale mapping -']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(route);
        await page.GetByTestId("component-action-add-map-Value").SelectOptionAsync("control:value");

        await Box(page, "value").ClickAsync();
        await page.GetByTestId("control-name").FillAsync("renamed");
        await page.Locator(".gfd-canvas").EvaluateAsync("canvas => {\n"
            + "  canvas.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerId: 1, buttons: 1 }));\n"
            + "  canvas.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, pointerId: 1, buttons: 0 }));\n"
            + "}");
        var mapping = page.GetByTestId("component-action-add-map-Value");
        await Assertions.Expect(mapping).ToContainTextAsync("Missing control: value");
        await mapping.SelectOptionAsync("control:renamed");

        await Box(page, "renamed").ClickAsync();
        await page.Locator("button.gfd-delete").ClickAsync();
        await Assertions.Expect(mapping).ToContainTextAsync("Missing control: renamed");
        await mapping.SelectOptionAsync("");
        await Assertions.Expect(mapping).ToHaveValueAsync("");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_matches_published_routes_case_insensitively_and_preserves_authored_casing()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        // The first segment keeps its historical alphanumeric start, while legacy routes may
        // begin '_' or '-' in later segments.
        var publishedReadRoute = $"CaseRead-{suffix}/_legacy";
        var publishedAddRoute = $"CaseAdd-{suffix}/-legacy";
        var authoredReadRoute = publishedReadRoute.ToLowerInvariant();
        var authoredAddRoute = publishedAddRoute.ToLowerInvariant();
        await PublishEndpointAsync(page, "Case read", "GET", publishedReadRoute, "CASE READ");
        await PublishEndpointAsync(page, "Case add", "POST", publishedAddRoute, "CASE ADD");

        var html = $"""
            <div data-gridlet="2" data-name="Case-sensitive form" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-source href="{authoredReadRoute}"></gridlet-source>
              <gridlet-action name="add" method="POST" href="{authoredAddRoute}"></gridlet-action>
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Case-sensitive form", html);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        var sourceRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "GET" && request.Url.EndsWith('/' + authoredReadRoute, StringComparison.Ordinal));
        await page.Locator("button.tree-item[title^='Case-sensitive form -']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();

        await sourceRequestTask;
        await Assertions.Expect(page.GetByTestId("component-source")).ToHaveValueAsync(publishedReadRoute);
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(publishedAddRoute);

        await page.GetByTestId("component-view-preview").ClickAsync();
        var actionRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + authoredAddRoute, StringComparison.Ordinal));
        await page.Locator("[data-name='send']").ClickAsync();
        await actionRequestTask;
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        Assert.Equal("CASE ADD", fixture.Provider.LastQuerySql);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_keeps_an_action_pending_across_preview_redraws()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"form-pending-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Pending add", "POST", route, "job-wait");
        var html = $"""
            <div data-gridlet="2" data-name="Pending form" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}"></gridlet-action>
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Pending form", html);
        var actionRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal))
            {
                actionRequests.Add(request);
            }
        };

        fixture.Provider.PrepareLongQuery();
        try
        {
            await page.GotoAsync("/gridlet/");
            var section = page.Locator("details").Filter(
                new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
            await section.Locator("summary").First.ClickAsync();
            await page.Locator("button.tree-item[title^='Pending form -']").ClickAsync();
            await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();
            await page.GetByTestId("component-view-preview").ClickAsync();

            var requestTask = page.WaitForRequestAsync(request =>
                request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
            await page.Locator("[data-name='send']").ClickAsync();
            await requestTask;
            await Assertions.Expect(page.Locator(".gfd-action-status"))
                .ToContainTextAsync("add in progress");
            await Assertions.Expect(page.Locator("[data-name='send']")).ToBeDisabledAsync();

            // Both a mode switch and the resulting canvas redraw replace the button element. The
            // operation-level state must keep its replacement disabled while the write is pending.
            await page.GetByTestId("component-view-design").ClickAsync();
            await page.GetByTestId("component-view-preview").ClickAsync();
            await Assertions.Expect(page.Locator("[data-name='send']")).ToBeDisabledAsync();
            await page.Locator("[data-name='send']").EvaluateAsync(
                "element => { for (let i = 0; i < 2; i++) element.dispatchEvent(new MouseEvent('click', { bubbles: true })); }");
            Assert.Single(actionRequests);

            fixture.Provider.ReleaseLongQuery();
            await Assertions.Expect(page.Locator(".gfd-action-status"))
                .ToHaveTextAsync("add completed successfully.");
            Assert.Single(actionRequests);
        }
        finally
        {
            fixture.Provider.ReleaseLongQuery();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Consumer_runtime_locks_every_button_for_an_operation_until_the_delayed_write_finishes()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"consumer-pending-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Consumer pending add", "POST", route, "job-wait");
        var html = $"""
            <div data-gridlet="2" data-name="Consumer pending" data-layout="free" style="width: 420px; height: 150px;">
              <gridlet-action name="add" method="POST" href="{route}"></gridlet-action>
              <button type="button" data-name="first" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">First</button>
              <button type="button" data-name="second" data-action="ADD" style="left: 150px; top: 16px; width: 120px; height: 30px;">Second</button>
              <button type="button" data-name="locked" data-action="add" disabled style="left: 284px; top: 16px; width: 120px; height: 30px;">Locked</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Consumer pending", html);
        var requests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal)) requests.Add(request);
        };

        fixture.Provider.PrepareLongQuery();
        try
        {
            await page.GotoAsync($"/gridlet/components/{id}");
            var requestTask = page.WaitForRequestAsync(request =>
                request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
            await page.Locator("[data-name='first']").ClickAsync();
            await requestTask;

            await Assertions.Expect(page.Locator("[data-name='first']")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("[data-name='second']")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("[data-name='locked']")).ToBeDisabledAsync();
            await page.Locator("[data-name='second']").EvaluateAsync(
                "element => element.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
            Assert.Single(requests);

            fixture.Provider.ReleaseLongQuery();
            await Assertions.Expect(page.Locator(".gridlet-action-status"))
                .ToHaveTextAsync("add completed successfully.");
            await Assertions.Expect(page.Locator("[data-name='first']")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("[data-name='second']")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("[data-name='locked']")).ToBeDisabledAsync();
        }
        finally
        {
            fixture.Provider.ReleaseLongQuery();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Unnamed_action_buttons_have_the_same_write_behavior_in_designer_and_consumer()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"unnamed-action-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Unnamed add", "POST", route, "UNNAMED ADD");
        var html = $"""
            <div data-gridlet="2" data-name="Unnamed action" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-action name=" add " method="POST" href="{route}"></gridlet-action>
              <button type="button" data-action=" ADD " style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Unnamed action", html);
        var actionRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal)) actionRequests.Add(request);
        };

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Unnamed action -']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(route);
        await page.GetByTestId("component-view-preview").ClickAsync();
        var designerRequest = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
        await page.Locator("button[data-action='add']").ClickAsync();
        await designerRequest;
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add completed successfully.");

        await page.GotoAsync($"/gridlet/components/{id}");
        var consumerRequest = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
        await page.Locator("button[data-action=' ADD ']").ClickAsync();
        await consumerRequest;
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        Assert.Equal(2, actionRequests.Count);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Undeclared_action_bindings_are_explicitly_invalid_and_never_reactivate()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var html = """
            <div data-gridlet="2" data-name="Undeclared action" data-layout="free" style="width: 360px; height: 100px;">
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Undeclared action", html);

        await page.GotoAsync($"/gridlet/components/{id}");
        await Assertions.Expect(page.Locator(".gridlet-runtime-message"))
            .ToContainTextAsync("add action is undeclared.");
        await Assertions.Expect(page.Locator("[data-name='send']")).ToBeDisabledAsync();

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Undeclared action -']").ClickAsync();
        await page.Locator("[data-name='send']").ClickAsync(new LocatorClickOptions { Force = true });
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.GetByTestId("control-action"))
            .ToContainTextAsync("Invalid action \"add\" (undeclared)");

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add action is undeclared.");
        await page.GetByTestId("component-view-code").ClickAsync();
        Assert.DoesNotContain("data-action=\"add\"",
            await page.GetByTestId("component-document-editor").InputValueAsync(), StringComparison.Ordinal);

        await page.GetByTestId("component-view-design").ClickAsync();
        await page.Locator("[data-name='send']").ClickAsync(new LocatorClickOptions { Force = true });
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("control-action").SelectOptionAsync("");
        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-action-status")).ToBeHiddenAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_disables_every_invalid_action_button_independently()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var html = """
            <div data-gridlet="2" data-name="Multiple invalid actions" data-layout="free" style="width: 360px; height: 100px;">
              <button type="button" data-name="first" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">First</button>
              <button type="button" data-name="second" data-action="remove" style="left: 150px; top: 16px; width: 120px; height: 30px;">Second</button>
            </div>
            """;
        await SaveComponentAsync(page, "Multiple invalid actions", html);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Multiple invalid actions -']").ClickAsync();
        await page.GetByTestId("component-view-preview").ClickAsync();

        await Assertions.Expect(page.Locator("[data-name='first']")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-name='second']")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator(".gfd-action-status")).ToContainTextAsync("undeclared");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_reports_endpoint_listing_unavailability_without_calling_a_valid_action_unpublished()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"listing-unavailable-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Listing unavailable add", "POST", route, "LISTING UNAVAILABLE");
        var html = $"""
            <div data-gridlet="2" data-name="Listing unavailable" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-action name="add" method="POST" href="{route}"></gridlet-action>
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Listing unavailable", html);
        var actionRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal)) actionRequests.Add(request);
        };
        await page.RouteAsync("**/gridlet/api/published", routeHandler => routeHandler.FulfillAsync(
            new RouteFulfillOptions { Status = 503, ContentType = "application/json", Body = "{\"error\":\"temporary listing failure\"}" }));

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Listing unavailable -']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add"))
            .ToContainTextAsync($"Not verified (POST {route})");
        await page.GetByTestId("component-view-preview").ClickAsync();
        await page.Locator("[data-name='send']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add failed: add action could not verify the published endpoint list.");
        Assert.Empty(actionRequests);

        browserPage.AssertNoUnexpectedErrors("Failed to load resource");
    }

    [Fact]
    public async Task Consumer_caches_the_published_endpoint_catalogue_across_action_clicks()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var suffix = Guid.NewGuid().ToString("n");
        var addRoute = $"cached-add-{suffix}";
        var updateRoute = $"cached-update-{suffix}";
        await PublishEndpointAsync(page, "Cached add", "POST", addRoute, "CACHED ADD");
        await PublishEndpointAsync(page, "Cached update", "PATCH", updateRoute, "CACHED UPDATE");
        var html = $"""
            <div data-gridlet="2" data-name="Cached catalogue" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-action name="add" method="POST" href="{addRoute}"></gridlet-action>
              <gridlet-action name="update" method="PATCH" href="{updateRoute}"></gridlet-action>
              <button type="button" data-name="add" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Add</button>
              <button type="button" data-name="update" data-action="update" style="left: 150px; top: 16px; width: 120px; height: 30px;">Update</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Cached catalogue", html);
        var catalogueRequests = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "GET" && request.Url.EndsWith("/gridlet/api/published/catalogue", StringComparison.Ordinal))
            {
                catalogueRequests++;
            }
        };

        await page.GotoAsync($"/gridlet/components/{id}");
        await page.Locator("[data-name='add']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        await page.Locator("[data-name='update']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("update completed successfully.");
        Assert.Equal(1, catalogueRequests);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Consumer_retries_the_published_endpoint_catalogue_after_a_transient_failure()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var suffix = Guid.NewGuid().ToString("n");
        var route = $"retry-catalogue-{suffix}";
        await PublishEndpointAsync(page, "Retry catalogue add", "POST", route, "RETRY CATALOGUE");
        var html = $"""
            <div data-gridlet="2" data-name="Retry catalogue" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-action name="add" method="POST" href="{route}"></gridlet-action>
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Retry catalogue", html);
        var catalogueRequests = 0;
        await page.RouteAsync("**/gridlet/api/published/catalogue", async routeHandler =>
        {
            catalogueRequests++;
            if (catalogueRequests == 1)
            {
                await routeHandler.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 503,
                    ContentType = "application/json",
                    Body = "{\"error\":\"temporary catalogue failure\"}",
                });
                return;
            }

            await routeHandler.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        method = "POST",
                        route,
                        enabled = true,
                        parameters = Array.Empty<object>(),
                    },
                }),
            });
        });

        await page.GotoAsync($"/gridlet/components/{id}");
        await page.Locator("[data-name='send']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add failed: temporary catalogue failure");
        await page.Locator("[data-name='send']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        Assert.Equal(2, catalogueRequests);

        browserPage.AssertNoUnexpectedErrors("Failed to load resource");
    }

    [Fact]
    public async Task Designer_can_open_and_repair_a_saved_document_with_a_legacy_invalid_route()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        var validRoute = $"repair-source-{suffix}";
        var invalidRoute = $"repair//source-{suffix}";
        await PublishEndpointAsync(page, "Repair source", "GET", validRoute, "REPAIR SOURCE");
        var html = $"""
            <div data-gridlet="2" data-name="Repairable legacy component" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-source href="{invalidRoute}"></gridlet-source>
              <span data-name="status" style="left: 16px; top: 16px; width: 200px; height: 24px;">Repair me</span>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Repairable legacy component", html);

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Repairable legacy component -']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();

        // A malformed saved document keeps the design surface inert until Code repair succeeds;
        // otherwise a canvas edit could appear to work and then be silently discarded.
        var controlCountBeforeRepair = await page.Locator(".gfd-control").CountAsync();
        await page.Locator(".gfd-palette-item[data-type='label']").EvaluateAsync("button => button.click()");
        Assert.Equal(controlCountBeforeRepair, await page.Locator(".gfd-control").CountAsync());

        await page.GetByTestId("component-view-code").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-document-editor")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("component-code-error"))
            .ToContainTextAsync("published route is unsafe or malformed");
        Assert.Contains(invalidRoute, await page.GetByTestId("component-document-editor").InputValueAsync(),
            StringComparison.Ordinal);

        var repaired = html.Replace(invalidRoute, validRoute, StringComparison.Ordinal);
        await page.GetByTestId("component-document-editor").FillAsync(repaired);
        await Assertions.Expect(page.GetByTestId("component-code-error")).ToBeHiddenAsync();

        await page.GetByTestId("component-view-design").ClickAsync();
        var saveResponse = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.EndsWith("/gridlet/api/components", StringComparison.Ordinal));
        await page.GetByTestId("component-save").ClickAsync();
        await saveResponse;

        var saved = await page.APIRequest.GetAsync($"/gridlet/api/components/{id}");
        Assert.True(saved.Ok, $"Reading the repaired component failed: {saved.Status}");
        var storedHtml = (await saved.JsonAsync())!.Value.GetProperty("html").GetString()!;
        Assert.Contains($"<gridlet-source href=\"{validRoute}\">", storedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidRoute, storedHtml, StringComparison.Ordinal);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_filters_legacy_invalid_routes_from_source_and_crud_selectors()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var suffix = Guid.NewGuid().ToString("n");
        var readRoute = $"selector-read-{suffix}";
        var addRoute = $"selector-add-{suffix}";
        await PublishEndpointAsync(page, "Selector read", "GET", readRoute, "SELECTOR READ");
        await PublishEndpointAsync(page, "Selector add", "POST", addRoute, "SELECTOR ADD");
        var html = $"""
            <div data-gridlet="2" data-name="Filtered selectors" data-layout="free" style="width: 360px; height: 100px;">
              <gridlet-source href="{readRoute}"></gridlet-source>
              <gridlet-action name="add" method="POST" href="{addRoute}"></gridlet-action>
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Filtered selectors", html);

        var listing = JsonSerializer.Serialize(new object[]
        {
            new { id = "valid-read", name = "Selector read", method = "GET", route = readRoute, enabled = true, parameters = Array.Empty<object>() },
            new { id = "valid-add", name = "Selector add", method = "POST", route = addRoute, enabled = true, parameters = Array.Empty<object>() },
            new { id = "legacy-read", name = "Legacy read", method = "GET", route = "sales//top", enabled = true, parameters = Array.Empty<object>() },
            new { id = "legacy-add", name = "Legacy add", method = "POST", route = "sales//top", enabled = true, parameters = Array.Empty<object>() },
        });
        await page.RouteAsync("**/gridlet/api/published", routeHandler => routeHandler.FulfillAsync(
            new RouteFulfillOptions { ContentType = "application/json", Body = listing }));

        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Filtered selectors -']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-canvas")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("component-source")).ToHaveValueAsync(readRoute);
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(addRoute);

        var sourceOptions = await page.GetByTestId("component-source").Locator("option").AllTextContentsAsync();
        var actionOptions = await page.GetByTestId("component-action-add").Locator("option").AllTextContentsAsync();
        Assert.DoesNotContain(sourceOptions, option => option.Contains("sales//top", StringComparison.Ordinal));
        Assert.DoesNotContain(actionOptions, option => option.Contains("sales//top", StringComparison.Ordinal));

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Consumer_action_parameter_names_are_case_insensitive_and_unknown_mappings_fail_closed()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"form-parameter-case-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Parameter case add", "POST", route, "PARAMETER CASE", [
            new { name = "Name", required = true, type = "string" },
        ]);
        var requests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal)) requests.Add(request);
        };

        var caseHtml = $"""
            <div data-gridlet="2" data-name="Parameter case form" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}">
                <param name="name" control="name">
              </gridlet-action>
              <input data-name="name" value="before" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="ADD" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        var caseId = await SaveComponentAsync(page, "Parameter case form", caseHtml);
        await page.GotoAsync($"/gridlet/components/{caseId}");
        await page.Locator("[data-name='name']").FillAsync("edited");
        var caseRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
        await page.Locator("[data-name='send']").ClickAsync();
        var caseRequest = await caseRequestTask;
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        using (var body = JsonDocument.Parse(caseRequest.PostData!))
        {
            Assert.Equal("edited", body.RootElement.GetProperty("Name").GetString());
        }

        var unknownHtml = $"""
            <div data-gridlet="2" data-name="Unknown mapping form" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}">
                <param name="Unknown" control="name">
              </gridlet-action>
              <input data-name="name" value="ignored" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="add" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        var unknownId = await SaveComponentAsync(page, "Unknown mapping form", unknownHtml);
        await page.GotoAsync($"/gridlet/components/{unknownId}");
        await page.Locator("[data-name='send']").ClickAsync();
        await Assertions.Expect(page.Locator(".gridlet-action-status"))
            .ToContainTextAsync("add failed: add action maps unknown parameter 'Unknown'.");
        Assert.Single(requests);

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_matches_action_parameter_case_and_rejects_unknown_mappings()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"designer-parameter-case-{Guid.NewGuid():n}";
        await PublishEndpointAsync(page, "Designer parameter case", "POST", route, "DESIGNER PARAMETER CASE", [
            new { name = "Name", required = true, type = "string" },
        ]);
        var caseHtml = $"""
            <div data-gridlet="2" data-name="Designer parameter case" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}">
                <param name="name" control="name">
              </gridlet-action>
              <input data-name="name" value="before" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="ADD" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        var caseId = await SaveComponentAsync(page, "Designer parameter case", caseHtml);
        await page.GotoAsync("/gridlet/");
        var section = page.Locator("details").Filter(
            new LocatorFilterOptions { Has = page.Locator("summary", new PageLocatorOptions { HasTextString = "Components" }) });
        await section.Locator("summary").First.ClickAsync();
        await page.Locator("button.tree-item[title^='Designer parameter case -']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-action-add")).ToHaveValueAsync(route);
        await page.GetByTestId("component-view-preview").ClickAsync();
        await page.Locator("[data-name='name']").FillAsync("designer value");
        var caseRequestTask = page.WaitForRequestAsync(request =>
            request.Method == "POST" && request.Url.EndsWith('/' + route, StringComparison.Ordinal));
        await page.Locator("[data-name='send']").ClickAsync();
        var caseRequest = await caseRequestTask;
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToHaveTextAsync("add completed successfully.");
        using (var body = JsonDocument.Parse(caseRequest.PostData!))
        {
            Assert.Equal("designer value", body.RootElement.GetProperty("Name").GetString());
        }

        var unknownHtml = $"""
            <div data-gridlet="2" data-name="Designer unknown mapping" data-layout="free" style="width: 420px; height: 130px;">
              <gridlet-action name="add" method="POST" href="{route}">
                <param name="Unknown" control="name">
              </gridlet-action>
              <input data-name="name" value="ignored" style="left: 16px; top: 16px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="add" style="left: 250px; top: 16px; width: 100px; height: 30px;">Send</button>
            </div>
            """;
        await page.GetByTestId("component-view-code").ClickAsync();
        await page.GetByTestId("component-document-editor").FillAsync(unknownHtml);
        await page.GetByTestId("component-view-preview").ClickAsync();
        await page.Locator("[data-name='send']").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-action-status"))
            .ToContainTextAsync("add failed: add action maps unknown parameter 'Unknown'.");

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Designer_rejects_duplicate_action_declarations_and_malformed_mappings()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Malformed action document",
            [Control("send", "button", props: new { text = "Send" })]);
        await page.GetByTestId("component-view-code").ClickAsync();
        var editor = page.GetByTestId("component-document-editor");
        var prefix = "<div data-gridlet=\"2\" data-name=\"Malformed action document\" "
            + "data-layout=\"free\" style=\"width: 360px; height: 100px;\">";
        var suffix = "<button data-name=\"send\" style=\"left: 16px; top: 16px; "
            + "width: 120px; height: 30px;\">Send</button></div>";

        var malformedDocuments = new[]
        {
            ("<gridlet-action name=\"add\" method=\"POST\" href=\"route\"></gridlet-action>"
                + "<gridlet-action name=\"add\" method=\"POST\" href=\"route\"></gridlet-action>",
                "declared more than once"),
            ("<gridlet-action name=\" add \" method=\"POST\" href=\"route\"></gridlet-action>"
                + "<gridlet-action name=\"ADD\" method=\"POST\" href=\"route\"></gridlet-action>",
                "declared more than once"),
            ("<gridlet-action name=\"add\" method=\"POST\" href=\"route\"><param name=\"Name\" control=\"\"></gridlet-action>",
                "empty control mapping"),
            ("<gridlet-action name=\"add\" method=\"POST\" href=\"route\"><param name=\"Name\" value=\"a\"><param name=\"name\" value=\"b\"></gridlet-action>",
                "declared more than once"),
            ("<gridlet-action name=\"add\" method=\"POST\" href=\"route\"><param name=\"Count\" value=\"NaN\" data-type=\"number\"></gridlet-action>",
                "finite number"),
        };

        foreach (var (actions, error) in malformedDocuments)
        {
            await editor.FillAsync(prefix + actions + suffix);
            await Assertions.Expect(page.GetByTestId("component-code-error"))
                .ToContainTextAsync(error);
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Consumer_runtime_rejects_malformed_typed_actions_before_fetching()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        var suffix = Guid.NewGuid().ToString("n");
        var route = $"malformed-runtime-{suffix}";
        var moduleName = $"malformed-lifecycle-{suffix}.js";
        await WriteModuleAsync(page, moduleName, """
            export default class MalformedLifecycle {
              constructor(component) { this.component = component; }
              connected() { this.component.on('load', () => this.component.notify('load event survived')); }
              onLoad() { this.component.field('marker').value = 'load handler survived'; }
            }
            """);
        var html = $"""
            <div data-gridlet="2" data-name="Malformed runtime action" data-layout="free" data-on-load="=onLoad()" style="width: 360px; height: 100px;">
              <gridlet-code src="{moduleName}"></gridlet-code>
              <gridlet-action name="add" method="POST" href="/{route}/">
                <param name="Count" value="1.5" data-type="integer">
              </gridlet-action>
              <input data-name="marker" value="" style="left: 16px; top: 52px; width: 220px; height: 30px;">
              <button type="button" data-name="send" data-action="add" style="left: 16px; top: 16px; width: 120px; height: 30px;">Send</button>
            </div>
            """;
        var id = await SaveComponentAsync(page, "Malformed runtime action", html);
        var publishedRequests = new List<IRequest>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/gridlet/pub/", StringComparison.Ordinal)) publishedRequests.Add(request);
        };

        await page.GotoAsync($"/gridlet/components/{id}");
        await Assertions.Expect(page.Locator(".gridlet-runtime-message"))
            .ToContainTextAsync("finite integer");
        await Assertions.Expect(page.Locator(".gridlet-runtime-notice"))
            .ToHaveTextAsync("load event survived");
        await Assertions.Expect(page.Locator("[data-name='marker']"))
            .ToHaveValueAsync("load handler survived");
        await Assertions.Expect(page.Locator("[data-name='send']")).ToBeDisabledAsync();
        Assert.Empty(publishedRequests);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The rule that decides whether a property is a value or a formula, which is the whole of the
    /// syntax: <c>=</c> in front makes it a formula, <c>'</c> in front makes it text whatever it
    /// looks like, and anything else is what it says.
    /// </summary>
    [Fact]
    public async Task Treats_a_property_as_a_formula_only_when_it_starts_with_an_equals_sign()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Syntax component",
            [Control("caption", "label", props: new { text = "literal" })]);

        await Canvas(page, "caption").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        var box = page.GetByTestId("expr-text");

        await box.FillAsync("Just text");
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("Just text");

        await box.FillAsync("=upper(\"abc\")");
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("ABC");

        // The only way to write a literal that begins with the character that starts a formula.
        await box.FillAsync("'=upper(\"abc\")");
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("=upper(\"abc\")");

        // The escape escapes itself, so text that really begins with an apostrophe is writable too.
        await box.FillAsync("''quoted");
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("'quoted");

        // What the box shows is what is stored, never what the formula worked out to: showing the
        // answer is how a formula gets destroyed by someone typing over it.
        await box.FillAsync("=upper(\"abc\")");
        await OpenPanelTabAsync(page, "Appearance");
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.GetByTestId("expr-text")).ToHaveValueAsync("=upper(\"abc\")");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A formula that fails produces an error value rather than nothing, and the value travels:
    /// anything built on it fails the same way.
    /// </summary>
    [Fact]
    public async Task Shows_the_spreadsheet_code_for_a_formula_that_fails()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Error component",
        [
            Control("divide", "label", bind: new { text = "=1/0" }, y: 10),
            Control("noFunction", "label", bind: new { text = "=nosuch(1)" }, y: 40),
            Control("noName", "label", bind: new { text = "=nothinghere + 1" }, y: 70),
            Control("notNumber", "label", bind: new { text = "=\"abc\" * 2" }, y: 100),
            Control("tooBig", "label", bind: new { text = "=1e308 * 10" }, y: 130),
            Control("unreadable", "label", bind: new { text = "=1 +* 2" }, y: 160),
            Control("itself", "label", bind: new { text = "=self.text" }, y: 190),
            Control("handled", "label", bind: new { text = "=iferror(1/0, \"safe\")" }, y: 220),
            // An error is the answer to anything built on it, so this label fails because the one
            // it names failed, not because there is anything wrong with it.
            Control("downstream", "label", bind: new { text = "=concat(\"total: \", divide.text)" }, y: 250),
        ]);

        await Assertions.Expect(Canvas(page, "divide")).ToHaveTextAsync("#DIV/0!");
        await Assertions.Expect(Canvas(page, "noFunction")).ToHaveTextAsync("#NAME?");
        await Assertions.Expect(Canvas(page, "noName")).ToHaveTextAsync("#NAME?");
        await Assertions.Expect(Canvas(page, "notNumber")).ToHaveTextAsync("#VALUE!");
        await Assertions.Expect(Canvas(page, "tooBig")).ToHaveTextAsync("#NUM!");
        await Assertions.Expect(Canvas(page, "unreadable")).ToHaveTextAsync("#SYNTAX?");
        await Assertions.Expect(Canvas(page, "itself")).ToHaveTextAsync("#CIRC!");
        await Assertions.Expect(Canvas(page, "handled")).ToHaveTextAsync("safe");
        await Assertions.Expect(Canvas(page, "downstream")).ToHaveTextAsync("#DIV/0!");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A control cannot sit at position #VALUE!. Where the shape of a property leaves no room for a
    /// code, the property keeps the value it last worked out to and the code is reported on the
    /// panel, where there is room for the reason as well.
    /// </summary>
    [Fact]
    public async Task Keeps_the_last_good_value_where_an_error_has_nowhere_to_show()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Fallback component",
            [Control("box", "label", bind: new { w = "=\"abc\" * 2" }, w: 240)]);

        // The width the document carries is the last one that worked, and it is what the control
        // keeps rather than collapsing to nothing.
        var width = await Box(page, "box").EvaluateAsync<double>(
            "element => element.getBoundingClientRect().width");
        Assert.Equal(240, width, 0);

        await Canvas(page, "box").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        var box = page.GetByTestId("expr-w");
        await Assertions.Expect(box).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bad"));
        await Assertions.Expect(box).ToHaveAttributeAsync("title", "This needs numbers on both sides.");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Both edges of an axis linked is a control that stretches: with each edge told where to be,
    /// the only thing left to give is the size between them. Set the way it is meant to be set,
    /// by dragging the handles, and read back out of the coordinates the Layout rows edit.
    /// </summary>
    [Fact]
    public async Task Stretches_a_control_whose_two_side_edges_are_both_linked()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Anchored component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 24, y: 10, w: 120, h: 24),
            // 24 in from the component's left edge and 30 in from its right, so the gaps the drags
            // preserve are the margins the layout already has.
            Control("grid1", "grid", props: new { columns = "Id" }, x: 24, y: 200, w: 666, h: 100),
        ]);

        await ShowAnchorHandlesAsync(page, "grid1");
        await DragAsync(page, page.GetByTestId("anchor-handle-left"), await EdgeCentreAsync(page, "frame", "left"));
        await ShowAnchorHandlesAsync(page, "grid1");
        await DragAsync(page, page.GetByTestId("anchor-handle-right"), await EdgeCentreAsync(page, "frame", "right"));
        await ShowAnchorHandlesAsync(page, "grid1");
        await DragAsync(page, page.GetByTestId("anchor-handle-top"), await EdgeCentreAsync(page, "button1", "bottom"));

        // A link has no store of its own: it is the formula, in the property that was there.
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("=24");
        await Assertions.Expect(page.GetByTestId("edge-right"))
            .ToHaveValueAsync("=component.width - 30");
        await Assertions.Expect(page.GetByTestId("edge-top")).ToHaveValueAsync("=button1.bottom + 166");

        // Nothing moved on the way: linking an edge keeps the gap the control already had.
        Assert.Equal(24, await OffsetAsync(page, "grid1", "left"), 0);
        Assert.Equal(666, await OffsetAsync(page, "grid1", "width"), 0);
        Assert.Equal(200, await OffsetAsync(page, "grid1", "top"), 0);

        // The component made wider, which is the whole point: the left edge stays where it was put,
        // the right edge keeps its 30px, and the control between them stretches.
        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new Position { X = 5, Y = 440 } });
        await page.GetByTestId("expr-width").FillAsync("900");

        Assert.Equal(24, await OffsetAsync(page, "grid1", "left"), 0);
        Assert.Equal(846, await OffsetAsync(page, "grid1", "width"), 0);
        Assert.Equal(200, await OffsetAsync(page, "grid1", "top"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Anchoring on the canvas: an edge handle dragged onto an edge of something else, which is
    /// the way anchoring is meant to be done. The dimension it leaves behind carries the offset and
    /// the way to take the anchor off again.
    /// </summary>
    [Fact]
    public async Task Drags_an_anchor_from_one_edge_onto_another()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Dragged anchor component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 24, y: 10, w: 120, h: 24),
            Control("grid1", "grid", props: new { columns = "Id" }, x: 24, y: 200, w: 300, h: 100),
        ]);

        await ShowAnchorHandlesAsync(page, "grid1");

        // The top edge onto the button's bottom edge. The control does not move: the gap it already
        // had becomes the offset.
        await DragAsync(page, page.GetByTestId("anchor-handle-top"), await EdgeCentreAsync(page, "button1", "bottom"));

        await Assertions.Expect(page.GetByTestId("anchor-offset-top")).ToHaveValueAsync("166");
        Assert.Equal(200, await OffsetAsync(page, "grid1", "top"), 0);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-top")).ToHaveValueAsync("=button1.bottom + 166");

        // The dimension is where the anchor is edited and where it is taken off.
        await page.GetByTestId("anchor-offset-top").FillAsync("20");
        await page.GetByTestId("anchor-offset-top").PressAsync("Enter");
        Assert.Equal(54, await OffsetAsync(page, "grid1", "top"), 0);

        // The dimension's controls appear under the pointer, so reaching them is two moves: onto the
        // dimension, then onto the control it just revealed. That is what a hand does with a mouse.
        await page.Locator(".gfd-dim-hit")
            .Filter(new LocatorFilterOptions { Has = page.GetByTestId("anchor-release-top") })
            .HoverAsync();
        await page.GetByTestId("anchor-release-top").ClickAsync();
        await Assertions.Expect(page.GetByTestId("edge-top")).ToHaveValueAsync("54");
        Assert.Equal(54, await OffsetAsync(page, "grid1", "top"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// One anchor on an axis moves the control; the second one stretches it. A control whose right
    /// edge follows the component's and whose left edge follows nothing slides along when the
    /// component is resized, carrying the width it had.
    /// </summary>
    [Fact]
    public async Task Moves_a_control_whose_far_edge_alone_is_anchored()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Sliding component",
            [Control("pager1", "pager", x: 500, y: 400, w: 180, h: 24)]);

        // 720 - 40 - 180: the right edge 40px in from the component's, nothing holding the left.
        await ShowAnchorHandlesAsync(page, "pager1");
        await DragAsync(page, page.GetByTestId("anchor-handle-right"), await EdgeCentreAsync(page, "frame", "right"));

        await OpenPanelTabAsync(page, "Appearance");
        // The link is on the right edge, so that is the row it reads out of.
        await Assertions.Expect(page.GetByTestId("edge-right"))
            .ToHaveValueAsync("=component.width - 40");
        Assert.Equal(500, await OffsetAsync(page, "pager1", "left"), 0);
        Assert.Equal(180, await OffsetAsync(page, "pager1", "width"), 0);

        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new Position { X = 5, Y = 440 } });
        await page.GetByTestId("expr-width").FillAsync("900");

        // It travelled with the edge it follows and kept its size, which is what anchoring one
        // edge of an axis means.
        Assert.Equal(680, await OffsetAsync(page, "pager1", "left"), 0);
        Assert.Equal(180, await OffsetAsync(page, "pager1", "width"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Reading an anchor back out of the formula it was written as, and what happens to the
    /// formula when the anchor is taken off again.
    /// </summary>
    [Fact]
    public async Task Reads_an_anchor_back_and_leaves_a_number_behind_when_it_is_removed()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Unanchored component",
        [
            Control("grid1", "grid", props: new { columns = "Id" },
                bind: new { w = "=component.width - 30 - self.x" }, x: 24, y: 200, w: 300, h: 100),
        ]);

        await Canvas(page, "grid1").ClickAsync();

        // A formula written by hand that says what a link says is drawn as that link. The left
        // edge is a plain number, which is not a link: it says where the edge is, not what it
        // follows, so it has no dimension of its own.
        await Assertions.Expect(page.GetByTestId("anchor-offset-right")).ToHaveValueAsync("-30");
        await Assertions.Expect(page.GetByTestId("anchor-release-right"))
            .ToHaveAttributeAsync("title", "Right edge linked to component. Click to unlink.");
        await Assertions.Expect(page.GetByTestId("anchor-release-left")).ToHaveCountAsync(0);
        Assert.Equal(666, await OffsetAsync(page, "grid1", "width"), 0);

        // Unlinking leaves the width it had, so nothing moves.
        await UnlinkAsync(page, "right");
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("expr-w")).ToHaveValueAsync("666");
        Assert.Equal(666, await OffsetAsync(page, "grid1", "width"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A handle is a thing to drag. Pressing one and letting go without moving must not anchor the
    /// edge to whatever happens to be within reach of it - and the frame's own edge is within
    /// reach of any control sitting near it.
    /// </summary>
    [Fact]
    public async Task Leaves_an_edge_alone_when_its_handle_is_only_clicked()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Clicked handle component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 24, y: 200, w: 300, h: 100)]);

        await ShowAnchorHandlesAsync(page, "grid1");

        // 24px from the frame's left edge, which is well inside the distance a drop would take.
        await page.GetByTestId("anchor-handle-left").ClickAsync();

        await Assertions.Expect(page.GetByTestId("anchor-release-left")).ToHaveCountAsync(0);
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("24");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A size formula of somebody's own says nothing about where the far edge is measured from, so
    /// it must not hide an anchor that lives in the position instead. An anchor the panel cannot
    /// show is an anchor nobody can take off again.
    /// </summary>
    [Fact]
    public async Task Reads_a_far_anchor_beside_a_size_formula_of_its_own()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Custom width component",
        [
            // A square, which is the documented use of self.h, beside the anchor the designer
            // writes for a right edge with nothing holding the left.
            Control("grid1", "grid", props: new { columns = "Id" },
                bind: new { w = "=self.h", x = "=component.width - 40 - self.w" },
                x: 500, y: 200, w: 120, h: 120),
        ]);

        await Canvas(page, "grid1").ClickAsync();
        await Assertions.Expect(page.GetByTestId("anchor-offset-right")).ToHaveValueAsync("-40");

        // And it can be taken off again, which is the part that was unreachable.
        await UnlinkAsync(page, "right");
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("560");
        await Assertions.Expect(page.GetByTestId("expr-w")).ToHaveValueAsync("=self.h");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The strip that reveals a dimension's controls takes presses, so it must not reach back over
    /// the control the dimension measures: a control that cannot be pressed cannot be moved.
    /// </summary>
    [Fact]
    public async Task Keeps_a_dimension_off_the_control_it_measures()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Tight dimension component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 24, y: 10, w: 120, h: 24),
            // Seven pixels from the frame's right edge, which is shorter than the smallest strip,
            // and a top edge aligned exactly with the button's, which measures nothing at all.
            Control("pager1", "pager",
                bind: new { x = "=component.width - 7 - self.w", y = "=button1.top" },
                x: 533, y: 10, w: 180, h: 30),
        ]);

        await Canvas(page, "pager1").ClickAsync();

        var covering = await page.EvaluateAsync<string>(
            "() => {"
            + " const box = document.querySelector('[data-control-box=\"pager1\"]').getBoundingClientRect();"
            + " return [...document.querySelectorAll('.gfd-dim-hit')].filter(strip => {"
            + "   const r = strip.getBoundingClientRect();"
            + "   return r.left < box.right && box.left < r.right"
            + "     && r.top < box.bottom && box.top < r.bottom;"
            + " }).map(strip => strip.className).join(', ');"
            + "}");
        Assert.True(covering.Length == 0,
            "A dimension's strip covers the control it measures: " + covering);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Auto-anchor, which is what the switch above the canvas turns on: a control dragged until
    /// one of its edges lands on another edge stays linked to it, and is let go of again by being
    /// pulled clear. Snapping is what finds the edge, so the switch means nothing without it.
    /// </summary>
    [Fact]
    public async Task Links_an_edge_a_drag_lands_on_and_lets_go_when_pulled_clear()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Auto anchor component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 300, y: 40, w: 120, h: 24),
            Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80),
        ]);

        await page.GetByTestId("component-autoanchor").ClickAsync();

        // The Layout rows, which are there whether a coordinate is a formula or a number. The
        // Settings page lists only the ones that are formulas, so a row would come and go with
        // the link and the test would be reading its own subject.
        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // Dragged so the grid's left edge comes within reach of the button's, but not onto it.
        await DragByAsync(page, Canvas(page, "grid1"), 256, 0);

        // It finished the last few pixels itself and stayed linked to what it landed on.
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("=button1.left");
        Assert.Equal(300, await OffsetAsync(page, "grid1", "left"), 0);

        // Pulled clear of the button altogether, the link goes with it and the grid is a plain
        // coordinate again. Clear of it on both sides: landing on the button's other edge would
        // be landing on an edge, which is the same gesture over again.
        await DragByAsync(page, Canvas(page, "grid1"), 160, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).Not.ToHaveValueAsync("=button1.left");
        await Assertions.Expect(page.GetByTestId("anchor-release-left")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Snapping stays available whatever the grid is doing, because half of what it lands on is
    /// the other controls and they are there either way. Auto-anchor is the one that needs it,
    /// because with nothing landing there is nothing to remember.
    /// </summary>
    [Fact]
    public async Task Keeps_snapping_available_with_the_grid_hidden()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Switch component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80)]);

        var snap = page.GetByTestId("component-snap");
        var autoAnchor = page.GetByTestId("component-autoanchor");

        await page.GetByTestId("component-grid").ClickAsync();
        await Assertions.Expect(snap).ToBeEnabledAsync();
        await Assertions.Expect(snap).ToHaveClassAsync(new Regex("active"));
        await Assertions.Expect(autoAnchor).ToBeEnabledAsync();

        await snap.ClickAsync();
        await Assertions.Expect(autoAnchor).ToBeDisabledAsync();
        await Assertions.Expect(autoAnchor).ToHaveAttributeAsync("title", "Auto-anchor needs snapping, which is off");

        await snap.ClickAsync();
        await Assertions.Expect(autoAnchor).ToBeEnabledAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Snapping onto an edge and remembering that landing are two things. With auto-anchor off a
    /// drag still lines up on its neighbour, and leaves no rule behind.
    /// </summary>
    [Fact]
    public async Task Snaps_onto_an_edge_without_linking_when_auto_anchor_is_off()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Snap only component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 300, y: 40, w: 120, h: 24),
            Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80),
        ]);

        // Auto-anchor left off, and the grid hidden so nothing rounds the drag but the edge.
        await page.GetByTestId("component-grid").ClickAsync();
        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        await DragByAsync(page, Canvas(page, "grid1"), 257, 0);

        // It finished the last three pixels onto the button's edge, and that is all it did.
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("300");
        await Assertions.Expect(page.GetByTestId("anchor-release-left")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// And it is not only the switch that stays available: with the grid hidden a drag still lands
    /// on another control's edge and stays linked to it, while landing on nothing keeps the exact
    /// place it was let go.
    /// </summary>
    [Fact]
    public async Task Snaps_to_another_control_with_the_grid_hidden()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Gridless snap component",
        [
            Control("button1", "button", props: new { text = "Save" }, x: 300, y: 40, w: 120, h: 24),
            Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80),
        ]);

        await page.GetByTestId("component-autoanchor").ClickAsync();
        await page.GetByTestId("component-grid").ClickAsync();

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // 40 + 257 is 297, three short of the button's left edge and inside the reach of it.
        await DragByAsync(page, Canvas(page, "grid1"), 257, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("=button1.left");
        Assert.Equal(300, await OffsetAsync(page, "grid1", "left"), 0);

        // Pulled clear of it, and with no grid to round to it keeps the exact distance it was
        // dragged rather than the nearest multiple of eight.
        await DragByAsync(page, Canvas(page, "grid1"), 157, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("457");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// And it is not only the switch: with the grid hidden a drag lands where it was let go rather
    /// than on a line nobody can see.
    /// </summary>
    [Fact]
    public async Task Stops_snapping_a_drag_once_the_grid_is_hidden()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Unsnapped component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // 13 is not a multiple of 8, so with the grid up the drag is held to 16.
        await DragByAsync(page, Canvas(page, "grid1"), 13, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("56");

        await page.GetByTestId("component-grid").ClickAsync();
        await DragByAsync(page, Canvas(page, "grid1"), 13, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("69");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A press on a control that is already selected and goes nowhere turns its handles over:
    /// the ones that size it, and the ones that say what its edges follow.
    /// </summary>
    [Fact]
    public async Task Turns_the_handles_over_on_a_second_click()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Handle component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-handle").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("anchor-handle-left")).ToHaveCountAsync(0);

        await Canvas(page, "grid1").ClickAsync();
        await Assertions.Expect(page.GetByTestId("anchor-handle-left")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".gfd-handle")).ToHaveCountAsync(0);

        await Canvas(page, "grid1").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-handle").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("anchor-handle-left")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Every edge a drag off this handle could land on, read off the dots it puts out while the
    /// button is still down. The drag is then let go over nothing, which links nothing.
    /// </summary>
    private static async Task<string[]> OfferedTargetsAsync(IPage page, string name, string edge)
    {
        await ShowAnchorHandlesAsync(page, name);
        var handle = await page.GetByTestId($"anchor-handle-{edge}").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The handle is not on the canvas.");
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await page.Mouse.DownAsync();
        var offered = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.gfd-anchor-target')]"
            + ".map(dot => dot.dataset.target + ':' + dot.dataset.edge)");
        await page.Mouse.UpAsync();
        return offered;
    }

    /// <summary>
    /// Takes a link off from its own dimension, which is two moves: onto the dimension, then onto
    /// the control it reveals. That is what a hand does with a mouse.
    /// </summary>
    private static async Task UnlinkAsync(IPage page, string edge)
    {
        await page.Locator(".gfd-dim-hit")
            .Filter(new LocatorFilterOptions { Has = page.GetByTestId($"anchor-release-{edge}") })
            .HoverAsync();
        await page.GetByTestId($"anchor-release-{edge}").ClickAsync();
    }

    /// <summary>A press, a move and a release, from the middle of whatever is being dragged.</summary>
    private static async Task DragByAsync(IPage page, ILocator from, float dx, float dy)
    {
        var box = await from.BoundingBoxAsync()
            ?? throw new InvalidOperationException("There is nothing there to drag.");
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + dx, y + dy, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
    }

    /// <summary>Column grips overlap a header edge; start inside the grip rather than on that edge.</summary>
    private static async Task DragGripByAsync(IPage page, ILocator from, float dx, float dy)
    {
        var box = await from.BoundingBoxAsync()
            ?? throw new InvalidOperationException("There is nothing there to drag.");
        var x = box.X + 1;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + dx, y + dy, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Links are a dependency graph, and a control is never offered what already follows it. A
    /// follower's edges move with the drag, so snapping onto one would move the control, which
    /// moves the follower, which moves the edge again: the control shakes instead of settling.
    /// </summary>
    [Fact]
    public async Task Never_links_a_control_to_something_that_follows_it()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Chained component",
        [
            Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80),
            // Directly downstream of the grid, and then one more step downstream of that, so the
            // indirect case is covered as well as the direct one.
            Control("button1", "button", props: new { text = "Save" },
                bind: new { x = "=grid1.right + 16" }, x: 256, y: 200, w: 120, h: 24),
            Control("label1", "label", props: new { text = "After" },
                bind: new { x = "=button1.right + 16" }, x: 392, y: 200, w: 80, h: 24),
        ]);

        await page.GetByTestId("component-autoanchor").ClickAsync();

        // Neither the control that follows the grid nor the one that follows that one is on offer.
        var offered = await OfferedTargetsAsync(page, "grid1", "left");
        Assert.DoesNotContain("button1:left", offered);
        Assert.DoesNotContain("button1:right", offered);
        Assert.DoesNotContain("label1:left", offered);
        Assert.DoesNotContain("label1:right", offered);
        Assert.Contains("frame:left", offered);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // Dragged far enough to pass right under both of them. Nothing latches on, and the grid
        // ends up exactly where the drag asked for rather than somewhere it settled into.
        await DragByAsync(page, Canvas(page, "grid1"), 96, 0);

        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("136");
        Assert.Equal(136, await OffsetAsync(page, "grid1", "left"), 0);

        // And the two that follow it kept up, which is the part that has to keep working.
        Assert.Equal(352, await OffsetAsync(page, "button1", "left"), 0);
        Assert.Equal(488, await OffsetAsync(page, "label1", "left"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Every edge reads out of its own row. A link on the right edge with nothing holding the left
    /// is stored in the position, and printing that under "Left" said the left edge was linked when
    /// it was the right one: the rows read the edges, not the properties that carry them.
    /// </summary>
    [Fact]
    public async Task Shows_the_right_and_bottom_edges_beside_the_four_that_store_them()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Edge row component",
        [
            Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 100, w: 200, h: 80),
            // Its right edge already follows the component's, written the way the designer writes
            // a link with nothing holding the left edge.
            Control("pager1", "pager", bind: new { x = "=component.width - 40 - self.w" },
                x: 500, y: 400, w: 180, h: 24),
        ]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // 40 + 200 and 100 + 80, worked out rather than stored.
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("240");
        await Assertions.Expect(page.GetByTestId("edge-bottom")).ToHaveValueAsync("180");

        // Setting one resizes, because the width is what is free to give.
        await page.GetByTestId("edge-right").FillAsync("300");
        await page.GetByTestId("edge-right").PressAsync("Tab");
        await Assertions.Expect(page.GetByTestId("expr-w")).ToHaveValueAsync("260");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("40");
        Assert.Equal(300, await OffsetAsync(page, "grid1", "left") + await OffsetAsync(page, "grid1", "width"), 0);

        // A linked edge is a number like any other, and setting it moves the edge along whatever it
        // follows instead of cutting it loose: the offset changes, the link does not.
        await Canvas(page, "pager1").ClickAsync();
        // The right edge is the linked one, so it is the row that says what it follows. The bottom
        // edge is a number, because nothing holds it.
        await Assertions.Expect(page.GetByTestId("edge-right"))
            .ToHaveValueAsync("=component.width - 40");
        await Assertions.Expect(page.GetByTestId("edge-bottom")).ToHaveValueAsync("424");

        await page.GetByTestId("edge-right").FillAsync("700");
        await page.GetByTestId("edge-right").PressAsync("Tab");
        await Assertions.Expect(page.GetByTestId("edge-right"))
            .ToHaveValueAsync("=component.width - 20");
        // The left edge did not move, so the width is what took the 20.
        Assert.Equal(500, await OffsetAsync(page, "pager1", "left"), 0);
        Assert.Equal(200, await OffsetAsync(page, "pager1", "width"), 0);

        // And it is marked the way every other row a formula decides is marked, so a glance down
        // the panel says which edges are being decided for it.
        await Assertions.Expect(page.Locator(".gfd-row.bound")
            .Filter(new LocatorFilterOptions { Has = page.GetByTestId("edge-right") }))
            .ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".gfd-row.bound")
            .Filter(new LocatorFilterOptions { Has = page.GetByTestId("edge-bottom") }))
            .ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Eight handles, one per side and one per corner, because a resize moves an edge and every
    /// edge has to be reachable. Moving a left edge is a different act from changing a width, and
    /// with only the three that were here it could not be asked for.
    /// </summary>
    [Fact]
    public async Task Offers_a_resize_handle_on_every_side_and_corner()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Handle count component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 200, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();

        foreach (var handle in new[] { "nw", "n", "ne", "e", "se", "s", "sw", "w" })
        {
            await Assertions.Expect(page.Locator($".gfd-handle-{handle}")).ToBeVisibleAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A handle moves the edge it is on and leaves the opposite one where it was. The west handle
    /// moves the left edge, which is what no handle could do before.
    /// </summary>
    [Fact]
    public async Task Moves_only_the_edge_the_handle_is_on()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Edge resize component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 200, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await Canvas(page, "grid1").ClickAsync();
        await Canvas(page, "grid1").ClickAsync();

        // The right edge out by 40. The left edge does not move, so the width takes it.
        await DragByAsync(page, page.Locator(".gfd-handle-e"), 40, 0);
        Assert.Equal(200, await OffsetAsync(page, "grid1", "left"), 0);
        Assert.Equal(240, await OffsetAsync(page, "grid1", "width"), 0);

        // The left edge in by 40. The right edge does not move, so the width takes it again.
        await DragByAsync(page, page.Locator(".gfd-handle-w"), 40, 0);
        Assert.Equal(240, await OffsetAsync(page, "grid1", "left"), 0);
        Assert.Equal(200, await OffsetAsync(page, "grid1", "width"), 0);

        // And the same on the other axis, from the top.
        await DragByAsync(page, page.Locator(".gfd-handle-n"), 0, 24);
        Assert.Equal(224, await OffsetAsync(page, "grid1", "top"), 0);
        Assert.Equal(56, await OffsetAsync(page, "grid1", "height"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Dragging an edge that follows something changes what it is measured from rather than being
    /// refused. The link survives the drag, and the opposite edge still does not move.
    /// </summary>
    [Fact]
    public async Task Resizes_a_linked_edge_by_moving_it_along_what_it_follows()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Linked resize component",
        [
            // Its right edge follows the component's, with nothing holding the left, so the whole
            // control slides when the component is resized.
            Control("grid1", "grid", props: new { columns = "Id" },
                bind: new { x = "=component.width - 40 - self.w" }, x: 480, y: 200, w: 200, h: 80),
        ]);

        await Canvas(page, "grid1").ClickAsync();
        await Canvas(page, "grid1").ClickAsync();
        await Canvas(page, "grid1").ClickAsync();

        // 720 - 40 is 680, so the right edge starts there and the left at 480.
        Assert.Equal(480, await OffsetAsync(page, "grid1", "left"), 0);

        await DragByAsync(page, page.Locator(".gfd-handle-e"), -24, 0);

        // The link is still a link, the offset moved, and the left edge stayed put.
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-right"))
            .ToHaveValueAsync("=component.width - 64");
        Assert.Equal(480, await OffsetAsync(page, "grid1", "left"), 0);
        Assert.Equal(176, await OffsetAsync(page, "grid1", "width"), 0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The panel is the readout for what the canvas is doing, so it follows a drag rather than
    /// waiting for it to finish. Read while the button is still down, which is the only moment
    /// that distinguishes following from catching up.
    /// </summary>
    [Fact]
    public async Task Follows_a_drag_in_the_panel_while_it_is_happening()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Live panel component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("40");

        var box = await Canvas(page, "grid1").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The grid is not on the canvas.");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 80, box.Y + box.Height / 2, new MouseMoveOptions { Steps = 8 });

        // Still held down, and the panel already says where the control is.
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("120");
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("320");

        await page.Mouse.UpAsync();
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("120");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The same while resizing, and on the row that is not being dragged: moving the right edge
    /// changes the width as it goes, and the left edge stays where it was throughout.
    /// </summary>
    [Fact]
    public async Task Follows_a_resize_in_the_panel_while_it_is_happening()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Live resize component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 40, y: 200, w: 200, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        var handle = await page.Locator(".gfd-handle-e").BoundingBoxAsync()
            ?? throw new InvalidOperationException("The east handle is not on the canvas.");
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handle.X + handle.Width / 2 + 40, handle.Y + handle.Height / 2, new MouseMoveOptions { Steps = 8 });

        await Assertions.Expect(page.GetByTestId("expr-w")).ToHaveValueAsync("240");
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("280");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("40");

        await page.Mouse.UpAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Snapping lands the edge being dragged on the grid, not the corner the control happens to
    /// start at. A control whose position is off the grid is the case someone turns snapping on to
    /// get out of, and measuring every edge from the corner leaves the far edge off it too.
    /// </summary>
    [Fact]
    public async Task Snaps_the_edge_being_dragged_rather_than_the_corner()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Off grid component",
            // 5 and 203 are both off the grid, so the right edge starts on 208 and the left does not.
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 5, y: 200, w: 203, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // Six to the right of 208 is 214, and the nearest grid line to that is 216.
        await DragByAsync(page, page.Locator(".gfd-handle-e"), 6, 0);
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("216");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("5");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A resize stopped by the canvas edge or by the smallest size stops the edge being dragged.
    /// Holding the position and the size back separately lets one of them take the whole distance
    /// while the other stops, which walks the opposite edge across the canvas.
    /// </summary>
    [Fact]
    public async Task Holds_the_opposite_edge_still_when_a_resize_is_stopped()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Clamped resize component",
            [Control("grid1", "grid", props: new { columns = "Id" }, x: 16, y: 200, w: 96, h: 80)]);

        await Canvas(page, "grid1").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        // Dragged well past the left of the canvas. The left edge stops at zero and the right edge
        // stays on 112, rather than being pushed out by everything the left edge could not take.
        await DragByAsync(page, page.Locator(".gfd-handle-w"), -80, 0);
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("0");
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("112");

        // And the other way: dragged past the right edge, the left stops one grid step short of it
        // and the right edge has still not moved.
        await DragByAsync(page, page.Locator(".gfd-handle-w"), 200, 0);
        await Assertions.Expect(page.GetByTestId("edge-right")).ToHaveValueAsync("112");
        await Assertions.Expect(page.GetByTestId("edge-left")).ToHaveValueAsync("104");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A component the person filling it in can resize, which the browser does through the grip
    /// CSS puts in its corner rather than through anything the designer is told about. What is
    /// anchored to the component's own edges has to follow that too, or Preview would show a
    /// layout that only holds together at the size it was drawn.
    /// </summary>
    [Fact]
    public async Task Follows_the_component_s_edges_when_preview_is_resized()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Resizable component",
        [
            // Anchored the way the designer writes it: the right edge 40px in from the
            // component's, with nothing holding the left, so the pager travels rather than grows.
            Control("pager1", "pager", bind: new { x = "=component.width - 40 - self.w" },
                x: 500, y: 400, w: 180, h: 24),
        ], resizable: true);

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(Box(page, "pager1")).ToHaveCSSAsync("left", "500px");

        // The grip's own doing, which is an inline size on the canvas and no event at all.
        await page.Locator(".gfd-canvas").EvaluateAsync("element => { element.style.width = '900px'; }");

        // 900 - 40 - 180. It travelled with the edge it follows and kept the width it had.
        await Assertions.Expect(Box(page, "pager1")).ToHaveCSSAsync("left", "680px");
        await Assertions.Expect(Box(page, "pager1")).ToHaveCSSAsync("width", "180px");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Selects a control and turns its handles over to the anchor ones, which is a click to select
    /// and a second click on the same control. The dimensions are drawn either way; the handles
    /// that put a link on are the ones behind the second click.
    /// </summary>
    private static async Task ShowAnchorHandlesAsync(IPage page, string name)
    {
        await Canvas(page, name).ClickAsync();
        await Canvas(page, name).ClickAsync();
        await Assertions.Expect(page.GetByTestId("anchor-handle-left")).ToBeVisibleAsync();
    }

    /// <summary>The middle of one edge of a control, in page coordinates, which is where an anchor
    /// handle sits and what an anchor drag aims at.</summary>
    private static async Task<(float X, float Y)> EdgeCentreAsync(IPage page, string name, string edge)
    {
        var of = name == "frame" ? page.Locator(".gfd-canvas") : Box(page, name);
        var box = await of.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"{name} is not on the canvas.");
        return edge switch
        {
            "left" => (box.X, box.Y + box.Height / 2),
            "right" => (box.X + box.Width, box.Y + box.Height / 2),
            "top" => (box.X + box.Width / 2, box.Y),
            _ => (box.X + box.Width / 2, box.Y + box.Height),
        };
    }

    /// <summary>
    /// A press, a move and a release. The move is in two steps because the designer only treats a
    /// drag as a drag once the pointer has actually gone somewhere.
    /// </summary>
    private static async Task DragAsync(IPage page, ILocator from, (float X, float Y) to)
    {
        var box = await from.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The handle is not on the canvas.");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(to.X, to.Y, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
    }

    /// <summary>
    /// Where a control's box sits inside the component, in the component's own pixels - read off
    /// the box rather than out of the document, so it is what the designer actually drew.
    /// </summary>
    private static async Task<double> OffsetAsync(IPage page, string name, string edge)
    {
        // Read again if there is nothing to read. The designer replaces its control elements every
        // time it redraws, and an element that has just been replaced reports no geometry at all -
        // an empty computed style, which parses to NaN rather than to the number the control has.
        // Each attempt resolves the box again, so a retry reads the live element rather than the
        // one that went away. Waiting cannot turn a wrong value into a right one: a control that
        // is genuinely somewhere else still reports where it genuinely is.
        for (var attempt = 1; ; attempt += 1)
        {
            var value = await Box(page, name).EvaluateAsync<double>(
                $"element => parseFloat(getComputedStyle(element).{edge})");
            if (!double.IsNaN(value)) return value;
            if (attempt >= 40) return value;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// A module's exports are the component's functions. Nothing registers them: writing
    /// <c>export function</c> is the whole step, and a component may name as many modules as it likes.
    /// </summary>
    [Fact]
    public async Task Calls_functions_the_component_s_own_modules_export()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "tax.js", """
            export const VAT_RATE = 0.2;
            export function vat(net) { return Number(net) * VAT_RATE; }
            """);
        await WriteModuleAsync(page, "labels.js", """
            export function shout(value) { return String(value).toUpperCase() + '!'; }
            // The component's own function wins over the built-in of the same name. Gridlet's own is
            // still reachable, under the name of the library it came from.
            export function json() { return 'mine'; }
            """);

        page = await OpenComponentAsync(browserPage, "Module component",
        [
            Control("fromFirst", "label", bind: new { text = "=vat(100)" }, y: 10),
            Control("constant", "label", bind: new { text = "=concat(VAT_RATE)" }, y: 40),
            Control("fromSecond", "label", bind: new { text = "=shout(\"ok\")" }, y: 70),
            Control("replaced", "label", bind: new { text = "=json(1)" }, y: 100),
            Control("original", "label", bind: new { text = "=gridlet.json(1)" }, y: 130),
        ],
            modules: ["tax.js", "labels.js"]);

        // A function and a constant from the first module, a function from the second: a helper
        // lives in whichever file it belongs in.
        await Assertions.Expect(Canvas(page, "fromFirst")).ToHaveTextAsync("20");
        await Assertions.Expect(Canvas(page, "constant")).ToHaveTextAsync("0.2");
        await Assertions.Expect(Canvas(page, "fromSecond")).ToHaveTextAsync("OK!");
        await Assertions.Expect(Canvas(page, "replaced")).ToHaveTextAsync("mine");
        await Assertions.Expect(Canvas(page, "original")).ToHaveTextAsync("1");

        // The built-in that was written over is said on the component, beside the module that did it,
        // together with the spelling that still reaches Gridlet's own.
        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 400 } });
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.Locator(".gfd-code-problem"))
            .ToContainTextAsync("replaces Gridlet's own json everywhere in this component. Write gridlet.json() for Gridlet's.");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A formula calls what a module exports and the public methods of the class the component runs.
    /// The difference between the two is <c>this</c>: a method is bound to the instance that was
    /// built with the component, so it can read what the constructor kept, while a plain function is
    /// called with no <c>this</c> at all and is handed what it needs by name.
    /// </summary>
    /// <remarks>
    /// The class is built while the component is being designed as well as while it runs, which is why
    /// the method answers here on a canvas nobody has previewed.
    /// </remarks>
    [Fact]
    public async Task Calls_what_a_module_exports_and_the_methods_of_the_class_it_runs()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "shape.js", """
            export default class Behaviour {
              #component;
              constructor(component) { this.#component = component; }

              // A public method of the behaviour class: a name a formula can call, bound to the
              // instance the component was handed to.
              size() { return this.#component.width + ' x ' + this.#component.height; }

              // Private, so it is not on the prototype and nothing can call it from a formula.
              #hidden() { return 'never'; }
            }

            // An export, which a formula can call as well.
            export function sizeOf(width, height) { return width + ' x ' + height; }

            // Nothing is passed to a formula's function invisibly, so a `this` is never there.
            export function loose() { return this.width; }

            // The component is an ordinary argument, and this is how a function reaches it.
            export function drawnSize(component) {
              const box = component.element.getBoundingClientRect();
              return Math.round(box.width) + ' x ' + Math.round(box.height);
            }
            """);

        page = await OpenComponentAsync(browserPage, "Exports component",
        [
            Control("method", "label", bind: new { text = "=size()" }, y: 10),
            Control("qualified", "label", bind: new { text = "=Behaviour.size()" }, y: 40),
            Control("exported", "label", bind: new { text = "=sizeOf(component.width, component.height)" }, y: 70),
            Control("loose", "label", bind: new { text = "=loose()" }, y: 100),
            Control("given", "label", bind: new { text = "=drawnSize(component)" }, y: 130),
            Control("missing", "label", bind: new { text = "=Behaviour.hidden()" }, y: 160),
        ],
            modules: ["shape.js"]);

        // The method, by its own name and by the name of the class it belongs to. Both read the
        // component the constructor was given, so component.width answers a module the way it answers a
        // formula.
        await Assertions.Expect(Canvas(page, "method")).ToHaveTextAsync("720 x 460");
        await Assertions.Expect(Canvas(page, "qualified")).ToHaveTextAsync("720 x 460");
        await Assertions.Expect(Canvas(page, "exported")).ToHaveTextAsync("720 x 460");
        await Assertions.Expect(Canvas(page, "loose")).ToHaveTextAsync("#VALUE!");
        // Handed the component by name, from a property formula on an ordinary control.
        await Assertions.Expect(Canvas(page, "given")).ToHaveTextAsync("720 x 460");
        await Assertions.Expect(Canvas(page, "missing")).ToHaveTextAsync("#NAME?");

        // The same names in Preview, because the class is the same class either way.
        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(Canvas(page, "method")).ToHaveTextAsync("720 x 460");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A method is how a formula reaches the component: it was built with it, so it can act on it from
    /// a handler without being handed anything.
    /// </summary>
    [Fact]
    public async Task Runs_a_method_of_the_component_s_own_class_from_a_handler()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "greeter.js", """
            export default class Greeter {
              #component;
              #greeting = 'hello';

              // The constructor stores; connected() acts.
              constructor(component) { this.#component = component; }

              greeting() { return this.#greeting; }

              shout(what) {
                this.#component.field('output').value = this.#greeting + ' ' + what;
              }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Greeting component",
        [
            Control("caption", "label", bind: new { text = "=greeting()" }, y: 10),
            Control("go", "button", props: new { text = "Go" },
                events: new { click = "=Greeter.shout(\"world\")" }, y: 50),
            Control("output", "label", props: new { text = "waiting" }, y: 90),
        ],
            modules: ["greeter.js"]);

        // A property calls the method while the component is being drawn; a handler calls it when the
        // component is running, and that one acts on the component the class was built with.
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("hello");

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Canvas(page, "go").ClickAsync();
        await Assertions.Expect(Canvas(page, "output")).ToHaveTextAsync("hello world");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Two definitions of the component author's own under one name are not ranked against each other.
    /// Whichever file loaded first is not an answer anybody could have predicted, so the bare name
    /// says it is ambiguous and says which spellings reach which.
    /// </summary>
    [Fact]
    public async Task Says_which_spelling_to_use_when_a_name_means_two_things()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "my.js", """
            export const LIMIT = 5;
            export default class My {
              total() { return 'from the class'; }
            }
            """);
        await WriteModuleAsync(page, "rates.js", """
            export const LIMIT = 9;
            export function total() { return 'from the module'; }
            """);

        page = await OpenComponentAsync(browserPage, "Ambiguous component",
        [
            Control("bare", "label", bind: new { text = "=total()" }, y: 10),
            Control("fromClass", "label", bind: new { text = "=My.total()" }, y: 40),
            Control("fromModule", "label", bind: new { text = "=rates.total()" }, y: 70),
            Control("bareValue", "label", bind: new { text = "=concat(LIMIT)" }, y: 100),
            Control("myValue", "label", bind: new { text = "=concat(my.LIMIT)" }, y: 130),
            Control("ratesValue", "label", bind: new { text = "=concat(rates.LIMIT)" }, y: 160),
        ],
            modules: ["my.js", "rates.js"]);

        await Assertions.Expect(Canvas(page, "bare")).ToHaveTextAsync("#NAME?");
        await Assertions.Expect(Canvas(page, "fromClass")).ToHaveTextAsync("from the class");
        await Assertions.Expect(Canvas(page, "fromModule")).ToHaveTextAsync("from the module");
        // A value is qualified the same way, by the file it is written in.
        await Assertions.Expect(Canvas(page, "bareValue")).ToHaveTextAsync("#NAME?");
        await Assertions.Expect(Canvas(page, "myValue")).ToHaveTextAsync("5");
        await Assertions.Expect(Canvas(page, "ratesValue")).ToHaveTextAsync("9");

        // Said once on the component as well as at the formula that went looking for it.
        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 400 } });
        await OpenPanelTabAsync(page, "Settings");
        var problems = page.Locator(".gfd-code-problem");
        await Assertions.Expect(problems).ToContainTextAsync(new[]
        {
            "total - is defined more than once",
            "LIMIT - is defined more than once",
        });

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A control keeps its own name. A module's file stem is a qualifier for calls, and the two
    /// live in different places in an expression - <c>duty.w</c> is a path to the control, and
    /// <c>duty.vat(1)</c> is a call into the file - so neither has to lose.
    /// </summary>
    [Fact]
    public async Task Tells_a_control_apart_from_a_module_with_the_same_name()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "duty.js", """
            export const RATE = 0.2;
            export function vat(net) { return Number(net) * 2; }
            """);

        page = await OpenComponentAsync(browserPage, "Namesake component",
        [
            Control("duty", "label", props: new { text = "the control" }, w: 300, y: 10),
            Control("path", "label", bind: new { text = "=concat(duty.w)" }, y: 40),
            Control("call", "label", bind: new { text = "=concat(duty.vat(1))" }, y: 70),
            Control("value", "label", bind: new { text = "=concat(RATE)" }, y: 100),
        ],
            modules: ["duty.js"]);

        await Assertions.Expect(Canvas(page, "path")).ToHaveTextAsync("300");
        await Assertions.Expect(Canvas(page, "call")).ToHaveTextAsync("2");
        await Assertions.Expect(Canvas(page, "value")).ToHaveTextAsync("0.2");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A constructor is somebody's code and it can throw. That is one thing to put right, said on
    /// the component, and not a canvas that stops drawing.
    /// </summary>
    [Fact]
    public async Task Keeps_drawing_when_a_module_s_constructor_throws()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "boom.js", """
            export function ok() { return 'the exports still work'; }

            export default class Boom {
              constructor() { throw new Error('the constructor gave up'); }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Throwing component",
        [
            Control("caption", "label", props: new { text = "still drawn" }, y: 10),
            Control("exported", "label", bind: new { text = "=ok()" }, y: 40),
        ],
            modules: ["boom.js"]);

        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("still drawn");
        await Assertions.Expect(Canvas(page, "exported")).ToHaveTextAsync("the exports still work");

        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 400 } });
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.Locator(".gfd-code-problem"))
            .ToContainTextAsync("the constructor gave up");

        browserPage.AssertNoUnexpectedErrors();
    }


    /// <summary>
    /// One module can hold the behaviour of more than one component. A component names the class it runs, so
    /// the other classes in the same file are read and not built.
    /// </summary>
    [Fact]
    public async Task Runs_only_the_class_a_component_names_out_of_a_module_that_holds_two()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "pair.js", """
            export function tint(what) { return 'tinted ' + what; }

            export class Red {
              colour() { return 'red'; }
            }

            export class Blue {
              colour() { return 'blue'; }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Red component",
        [
            Control("bare", "label", bind: new { text = "=colour()" }, y: 10),
            Control("qualified", "label", bind: new { text = "=Red.colour()" }, y: 40),
            Control("other", "label", bind: new { text = "=Blue.colour()" }, y: 70),
            Control("exported", "label", bind: new { text = "=tint(\"it\")" }, y: 100),
            Control("notAFunction", "label", bind: new { text = "=Red()" }, y: 130),
        ],
            modules: [new { module = "pair.js", @class = "Red" }]);

        await Assertions.Expect(Canvas(page, "bare")).ToHaveTextAsync("red");
        await Assertions.Expect(Canvas(page, "qualified")).ToHaveTextAsync("red");
        // Blue is written in the same file and this component does not run it, so there is nothing of
        // that name to call.
        await Assertions.Expect(Canvas(page, "other")).ToHaveTextAsync("#NAME?");
        // What the file exports is in scope either way: a class of it is attached, so the file is
        // one this component uses.
        await Assertions.Expect(Canvas(page, "exported")).ToHaveTextAsync("tinted it");
        // A class is offered on the Behaviour section to be run, not offered to a formula to call,
        // so exporting one by name adds no name of its own and clashes with nothing.
        await Assertions.Expect(Canvas(page, "notAFunction")).ToHaveTextAsync("#NAME?");

        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 400 } });
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.Locator(".gfd-code-problem")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The other half of the same file, in another component: the two components share a module and run
    /// different behaviour out of it.
    /// </summary>
    [Fact]
    public async Task Runs_the_other_class_of_the_same_module_in_another_component()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "pair.js", """
            export function tint(what) { return 'tinted ' + what; }

            export class Red {
              colour() { return 'red'; }
            }

            export class Blue {
              colour() { return 'blue'; }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Blue component",
        [
            Control("bare", "label", bind: new { text = "=colour()" }, y: 10),
            Control("qualified", "label", bind: new { text = "=Blue.colour()" }, y: 40),
        ],
            modules: [new { module = "pair.js", @class = "Blue" }]);

        await Assertions.Expect(Canvas(page, "bare")).ToHaveTextAsync("blue");
        await Assertions.Expect(Canvas(page, "qualified")).ToHaveTextAsync("blue");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The classes a module holds are offered under it on the Behaviour section, once the component
    /// names the file: what a module exports cannot be known without reading it, and reading every
    /// module in the workspace to draw a list would run all of them.
    /// </summary>
    [Fact]
    public async Task Offers_a_module_s_classes_under_it_once_the_component_names_the_file()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "sides.js", """
            export class Left {
              side() { return 'left'; }
            }

            export class Right {
              side() { return 'right'; }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Sides component",
            [Control("chosen", "label", bind: new { text = "=side()" }, y: 10)]);

        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(Canvas(page, "chosen")).ToHaveTextAsync("#NAME?");

        // Naming the file is what makes its classes knowable, so they appear beneath it.
        await page.GetByTestId("module-sides.js").CheckAsync();
        await Assertions.Expect(page.GetByTestId("module-class-sides.js-Left")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("module-class-sides.js-Right")).ToBeVisibleAsync();

        // One class of it, and not the file's own default, which sides.js does not have.
        await page.GetByTestId("module-class-sides.js-Right").CheckAsync();
        await page.GetByTestId("module-sides.js").UncheckAsync();
        await Assertions.Expect(Canvas(page, "chosen")).ToHaveTextAsync("right");
        await Assertions.Expect(page.GetByTestId("module-class-sides.js-Right")).ToBeCheckedAsync();

        browserPage.AssertNoUnexpectedErrors();
    }


    /// <summary>
    /// The second thing a class is given: Gridlet's own services, and any a module of this component
    /// offers. A service one module writes reaches every class the component runs, because services
    /// belong to the component rather than to the file they were written in.
    /// </summary>
    [Fact]
    public async Task Hands_a_class_the_services_the_component_offers()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "kit.js", """
            // A service of this module's own. Every class the component runs is handed it.
            export const services = {
              audit: { note(what) { return 'noted ' + what; } },
            };

            export default class Keeper {
              #services;
              constructor(component, services) { this.#services = services; }

              // Gridlet's own, kept per component and per browser.
              remember(what) {
                this.#services.storage.write('last', what);
                return this.#services.storage.read('last', 'nothing');
              }
            }
            """);

        await WriteModuleAsync(page, "reader.js", """
            export default class Reader {
              #services;
              constructor(component, services) { this.#services = services; }

              // kit.js wrote it; this class is in the same component, so it has it too.
              check() { return this.#services.audit.note('from another class'); }
            }
            """);

        page = await OpenComponentAsync(browserPage, "Services component",
        [
            Control("kept", "label", bind: new { text = "=remember(\"this one\")" }, y: 10),
            Control("shared", "label", bind: new { text = "=check()" }, y: 40),
        ],
            modules: ["kit.js", "reader.js"]);

        await Assertions.Expect(Canvas(page, "kept")).ToHaveTextAsync("this one");
        await Assertions.Expect(Canvas(page, "shared")).ToHaveTextAsync("noted from another class");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Two modules offering one service name is the same question as two functions of one name,
    /// and it gets the same answer: nothing is chosen for you. Neither is used, and the component says
    /// which files to look at.
    /// </summary>
    [Fact]
    public async Task Uses_neither_service_when_two_modules_offer_the_same_name()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await WriteModuleAsync(page, "first.js", """
            export const services = { audit: { note() { return 'first'; } } };

            export default class First {
              #services;
              constructor(component, services) { this.#services = services; }
              note() { return this.#services.audit.note(); }
            }
            """);

        await WriteModuleAsync(page, "second.js", """
            export const services = { audit: { note() { return 'second'; } } };
            """);

        page = await OpenComponentAsync(browserPage, "Clashing services component",
            [Control("note", "label", bind: new { text = "=note()" }, y: 10)],
            modules: ["first.js", "second.js"]);

        // The method runs and finds nothing there, which is one property in error and not a component
        // that stopped drawing.
        await Assertions.Expect(Canvas(page, "note")).ToHaveTextAsync("#VALUE!");

        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 400 } });
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.Locator(".gfd-code-problem"))
            .ToContainTextAsync("is offered as a service by first.js and second.js, so none of them is used.");

        browserPage.AssertNoUnexpectedErrors();
    }


    /// <summary>
    /// Six rows of a panel is enough to keep a rule in and not enough to write a stylesheet in, so
    /// the same text opens in a tab of its own: numbered lines, highlighting, and a list of what
    /// could come next. The box and the tab are two views of one string - what is typed in either
    /// styles the canvas at once, and the other one follows.
    /// </summary>
    [Fact]
    public async Task Opens_the_component_s_custom_css_in_a_tab_of_its_own()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Stylesheet component",
            [Control("go", "button", props: new { text = "Go" })],
            css: "[data-name=\"go\"] {\n  color: rgb(1, 2, 3);\n}");

        await OpenPanelTabAsync(page, "Appearance");
        await page.Locator(".gfd-section", new PageLocatorOptions { HasTextString = "Custom CSS" })
            .Locator("summary").First.ClickAsync();
        await page.GetByTestId("css-expand-@component").ClickAsync();

        var editor = page.Locator("[data-testid='component-css-editor'][data-css-target='@component']");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        await Assertions.Expect(editor).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("rgb\\(1, 2, 3\\)"));
        // Highlighted, not just held: a property is coloured as a property because of where it is.
        await Assertions.Expect(page.Locator(".gfd-code-highlight .gfd-tok-property").First)
            .ToHaveTextAsync("color");

        // Typing here restyles the canvas as it goes, the same as typing in the box does.
        await editor.FillAsync("[data-name=\"go\"] {\n  color: rgb(9, 8, 7);\n}");
        await Assertions.Expect(Canvas(page, "go")).ToHaveCSSAsync("color", "rgb(9, 8, 7)");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Component CSS is scoped without treating nested conditional rules as selectors. Root
    /// variables still land on the component surface, while keyframes remain global definitions.
    /// </summary>
    [Fact]
    public async Task Scopes_nested_authored_css_at_rules_without_corrupting_them()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Nested CSS component",
            [Control("go", "button", props: new { text = "Go" })],
            css: """
                :root { --nested-text: rgb(4, 5, 6); }
                .nested { color: var(--nested-text); animation: nested-pulse 1s; }
                @media (min-width: 0px) {
                  :root .nested { background-color: rgb(7, 8, 9); }
                }
                @supports (display: grid) {
                  .nested { outline: 2px solid rgb(10, 11, 12); }
                }
                @keyframes nested-pulse {
                  from { opacity: 0.4; }
                  to { opacity: 1; }
                }
                """);

        await Box(page, "go").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("expr-classes").FillAsync("nested");

        var button = Canvas(page, "go");
        await Assertions.Expect(button).ToHaveCSSAsync("color", "rgb(4, 5, 6)");
        await Assertions.Expect(button).ToHaveCSSAsync("background-color", "rgb(7, 8, 9)");
        await Assertions.Expect(button).ToHaveCSSAsync("outline-width", "2px");
        await Assertions.Expect(button).ToHaveCSSAsync("animation-name", "nested-pulse");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Root dimensions are authored CSS values, not only numbers. The designer accepts responsive
    /// units and writes them back without converting them to the last measured border box.
    /// </summary>
    [Fact]
    public async Task Round_trips_responsive_root_dimensions_as_authored_css()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Responsive component",
            [Control("go", "button", props: new { text = "Go" })]);

        await page.Locator(".gfd-canvas").ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 5, Y = 440 },
        });
        await OpenPanelTabAsync(page, "Appearance");
        await page.GetByTestId("expr-width").FillAsync("100vw");
        await page.GetByTestId("expr-height").FillAsync("calc(100vh - 16px)");

        await Assertions.Expect(page.GetByTestId("expr-width")).ToHaveValueAsync("100vw");
        await Assertions.Expect(page.GetByTestId("expr-height")).ToHaveValueAsync("calc(100vh - 16px)");
        await page.GetByTestId("component-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("tab-unsaved")).ToHaveCountAsync(0);

        var component = await page.APIRequest.GetAsync("/gridlet/api/components");
        Assert.True(component.Ok, $"Reading saved components failed: {component.Status}");
        var saved = (await component.JsonAsync())!.Value.EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "Responsive component");
        var html = saved.GetProperty("html").GetString()!;
        Assert.Contains("width: 100vw", html, StringComparison.Ordinal);
        Assert.Contains("height: calc(", html, StringComparison.Ordinal);
        Assert.Contains("100vh", html, StringComparison.Ordinal);
        Assert.Contains("16px", html, StringComparison.Ordinal);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// What could come next, offered where the caret is: this component's own selectors, then the
    /// properties CSS has, then what the property being written takes. The list suggests and never
    /// decides - nothing arrives without being chosen.
    /// </summary>
    [Fact]
    public async Task Suggests_this_component_s_selectors_and_what_a_property_takes()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Completing component",
            [Control("go", "button", props: new { text = "Go" })]);

        await OpenPanelTabAsync(page, "Appearance");
        await page.Locator(".gfd-section", new PageLocatorOptions { HasTextString = "Custom CSS" })
            .Locator("summary").First.ClickAsync();
        await page.GetByTestId("css-expand-@component").ClickAsync();

        var editor = page.Locator("[data-testid='component-css-editor'][data-css-target='@component']");
        var items = page.GetByTestId("css-completions").Locator(".gfd-complete-item");

        // A selector: the controls this component really has, by the name they carry.
        await editor.PressSequentiallyAsync("[data-name=\"g");
        await Assertions.Expect(items.First).ToContainTextAsync("[data-name=\"go\"]");
        await editor.PressAsync("Enter");
        await Assertions.Expect(editor).ToHaveValueAsync("[data-name=\"go\"]");

        // A property, and then what that property takes. Choosing the property asks the next
        // question straight away, because a property without a value is not a rule yet.
        await editor.PressSequentiallyAsync(" {\n  text-al");
        await Assertions.Expect(items.First).ToContainTextAsync("text-align");
        await editor.PressAsync("Enter");
        await Assertions.Expect(items.First).ToContainTextAsync("left");
        await editor.PressAsync("ArrowDown");
        await editor.PressAsync("Enter");
        await Assertions.Expect(editor).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("text-align: center"));

        // Escape puts the list away rather than choosing from it.
        await editor.PressSequentiallyAsync(";\n  colo");
        await Assertions.Expect(items.First).ToBeVisibleAsync();
        await editor.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("css-completions")).ToBeHiddenAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A control's own rules open the same way, in a tab that is about that control: its own
    /// selectors are offered first, and the panel's box holds whatever the tab was left holding.
    /// </summary>
    [Fact]
    public async Task Opens_a_control_s_custom_css_in_its_own_tab_and_keeps_the_panel_in_step()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Control stylesheet component",
        [
            Control("go", "button", props: new { text = "Go" }),
            Control("other", "label", y: 60),
        ]);

        await Box(page, "go").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await page.Locator(".gfd-section", new PageLocatorOptions { HasTextString = "Custom CSS" })
            .Locator("summary").First.ClickAsync();
        await page.Locator("[data-testid^='css-expand-']").ClickAsync();

        var editor = page.Locator("[data-testid='component-css-editor']:not([data-css-target='@component'])");
        await Assertions.Expect(editor).ToBeVisibleAsync();

        // The control this stylesheet is about comes first in the list.
        await editor.PressSequentiallyAsync("[data-name");
        await Assertions.Expect(page.GetByTestId("css-completions").Locator(".gfd-complete-item").First)
            .ToContainTextAsync("[data-name=\"go\"]");
        await editor.PressAsync("Escape");

        await editor.FillAsync("[data-name=\"go\"] {\n  opacity: 0.5;\n}");
        await Assertions.Expect(Canvas(page, "go")).ToHaveCSSAsync("opacity", "0.5");

        // Back on the component, the box in the panel holds what the tab was left holding.
        await page.Locator(".tab", new PageLocatorOptions { HasTextString = "Control stylesheet component" })
            .First.ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-css").First)
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("opacity: 0.5"));

        browserPage.AssertNoUnexpectedErrors();
    }


    /// <summary>
    /// What a component shows and what it is called are one page. Naming a control, pointing it at a
    /// column and setting the properties of its kind is one piece of work, and it used to be split
    /// across two tabs; the panel now has two pages, not three.
    /// </summary>
    [Fact]
    public async Task Puts_what_a_thing_shows_on_the_same_page_as_what_it_is_called()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Merged component",
        [
            Control("caption", "label", props: new { text = "Hello" }),
            Control("box", "textbox", y: 60),
        ]);

        await Assertions.Expect(page.Locator(".gfd-tabs button")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator(".gfd-tabs button[title='Data']")).ToHaveCountAsync(0);

        // The component: what it is called, where its rows come from, and what it runs, one after
        // the other on one page.
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.GetByTestId("component-source")).ToBeVisibleAsync();
        var panel = page.Locator(".gfd-properties-body");
        await Assertions.Expect(panel).ToContainTextAsync("Component");
        await Assertions.Expect(panel).ToContainTextAsync("Source");
        await Assertions.Expect(panel).ToContainTextAsync("Behaviour");

        // A text box: its value slot has no property of its own, so it is shown as Value beside
        // the placeholder and the rest of its kind's properties.
        await Box(page, "box").ClickAsync();
        await Assertions.Expect(panel).ToContainTextAsync("Value");
        await Assertions.Expect(panel).ToContainTextAsync("Properties");
        await Assertions.Expect(page.GetByTestId("control-name")).ToHaveValueAsync("box");

        // A label displays its Text, so its value is that property and there is one row for it
        // rather than two boxes writing the same thing.
        await Box(page, "caption").ClickAsync();
        await Assertions.Expect(page.GetByTestId("expr-text")).ToHaveCountAsync(1);
        await page.GetByTestId("expr-text").FillAsync("Renamed");
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("Renamed");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A colour chosen by a formula still has a swatch: the formula and the colour it produced are
    /// worth seeing together. A result that is not a colour has no swatch to show, so it is
    /// crossed out instead of quietly showing black.
    /// </summary>
    [Fact]
    public async Task Shows_the_colour_a_formula_produced_and_crosses_out_one_that_is_not()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Colour component",
            [
                Control("swatchLabel", "label", bind: new Dictionary<string, string>
                {
                    ["color.light"] = "=concat(\"#\", \"3366ff\")",
                }),
                Control("otherLabel", "label", x: 10, y: 60),
            ]);

        // Make the light variant the one on the canvas. The colour panel now deliberately shows
        // only that current theme rather than presenting both stored variants as simultaneous inputs.
        var theme = page.GetByTestId("component-theme");
        for (var attempts = 0; attempts < 3 && await theme.GetAttributeAsync("data-theme-mode") != "light"; attempts++)
        {
            await theme.ClickAsync();
        }

        await Canvas(page, "swatchLabel").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

        await Assertions.Expect(page.Locator(".gfd-heading", new() { HasTextString = "Colours (Light theme)" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("colour-color.dark")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("colour-not-defined-fill.light")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("colour-fill.light"))
            .ToHaveAttributeAsync("placeholder", "Not defined");

        // Colour rows use the same two-column track and input height as ordinary property rows.
        // Their fields therefore have identical widths and the same one-pixel inter-row gap.
        var colourLayout = await page.EvaluateAsync<double[]>("""
            () => {
              const box = selector => document.querySelector(selector).getBoundingClientRect();
              const width = box('.gfd-row[data-property="w"] .gfd-cell input');
              const height = box('.gfd-row[data-property="h"] .gfd-cell input');
              const text = box('.gfd-row [data-property="color.light"] .gfd-colour-control');
              const fill = box('.gfd-row [data-property="fill.light"] .gfd-colour-control');
              return [width.width, text.width, height.top - width.bottom, fill.top - text.bottom];
            }
            """);
        Assert.Equal(colourLayout[0], colourLayout[1]);
        Assert.Equal(colourLayout[2], colourLayout[3]);

        var fillControl = page.GetByTestId("colour-fill.light").Locator("xpath=..");
        var clearFill = fillControl.GetByRole(AriaRole.Button, new() { Name = "Clear background on the light theme" });
        await Assertions.Expect(clearFill).ToBeDisabledAsync();
        await Assertions.Expect(clearFill).ToHaveCSSAsync("opacity", "0");
        await fillControl.HoverAsync();
        await Assertions.Expect(clearFill).ToHaveCSSAsync("opacity", "0");

        // The slash is only the undefined marker; the swatch is still a working colour picker, now
        // the designer's own, which knows about opacity and the forms CSS spells colours in.
        var undefinedSwatch = page.GetByTestId("colour-not-defined-fill.light");
        await Assertions.Expect(undefinedSwatch).ToBeEnabledAsync();
        await undefinedSwatch.EvaluateAsync("swatch => { window.__fillSwatch = swatch; }");
        await undefinedSwatch.ClickAsync();
        var popover = page.GetByTestId("gfd-colour-pop");
        await Assertions.Expect(popover).ToBeVisibleAsync();
        var horizontalOverflow = await popover.EvaluateAsync<JsonElement>(
            """
            popover => {
              const right = popover.getBoundingClientRect().right
                - parseFloat(getComputedStyle(popover).paddingRight);
              return {
                overflowX: getComputedStyle(popover).overflowX,
                clientWidth: popover.clientWidth,
                scrollWidth: popover.scrollWidth,
                contentFits: popover.scrollWidth <= popover.clientWidth,
                offenders: [...popover.querySelectorAll('*')]
                  .filter(element => element.getBoundingClientRect().right > right + 0.5)
                  .map(element => ({
                    className: element.className?.baseVal || element.className,
                    right: element.getBoundingClientRect().right,
                    width: element.getBoundingClientRect().width,
                  })).slice(0, 8),
              };
            }
            """);
        Assert.Equal("hidden", horizontalOverflow.GetProperty("overflowX").GetString());
        Assert.True(horizontalOverflow.GetProperty("contentFits").GetBoolean(),
            $"picker content overflowed horizontally: {horizontalOverflow}");

        // A pick writes the property and the write redraws the panel, but the redraw must reach the
        // swatch in place: a swatch rebuilt underneath its own popover would leave that popover
        // pointing at a detached element.
        await page.GetByTestId("gfd-cp-hex").FillAsync("#224466");
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("#224466");
        Assert.True(await page.EvaluateAsync<bool>("window.__fillSwatch.isConnected"));
        Assert.Equal("colour-swatch-fill.light",
            await page.EvaluateAsync<string>("window.__fillSwatch.getAttribute('data-testid')"));

        // Current means the literal value the picker opened with, even when that value was empty
        // and therefore had no colour to parse or paint.
        await popover.GetByRole(AriaRole.Button,
            new() { Name = "Back to the colour this opened with" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("");
        await Assertions.Expect(page.GetByTestId("colour-not-defined-fill.light")).ToBeVisibleAsync();

        // Invalid current text is restored literally too; Current is undo-to-open, not merely a
        // shortcut to the last value that happened to parse as a colour.
        await page.GetByTestId("colour-fill.light").FillAsync("not a colour");
        await page.GetByTestId("colour-bad-fill.light").ClickAsync();
        await page.GetByTestId("gfd-cp-hex").FillAsync("#abcdef");
        await popover.GetByRole(AriaRole.Button,
            new() { Name = "Back to the colour this opened with" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("not a colour");
        await page.GetByTestId("gfd-cp-hex").FillAsync("#224466");

        // Opacity remains part of the editable literal after the redundant number boxes are gone.
        await page.GetByTestId("gfd-cp-hex").FillAsync("#22446680");
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("#22446680");
        await page.GetByTestId("gfd-cp-hex").FillAsync("#224466");
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("#224466");

        // The New preview is the one literal editor for every form CSS spells colours in.
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("rgb");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("rgb(34 68 102)");
        await page.GetByTestId("gfd-cp-hex").FillAsync("rgb(51 68 102)");
        await Assertions.Expect(page.GetByTestId("colour-fill.light"))
            .ToHaveValueAsync("rgb(51 68 102)");

        // The mode, visual editor, previews and favourites form one column. No parallel numeric
        // editor remains beside the visual controls.
        var pickerLayout = await popover.EvaluateAsync<JsonElement>(
            """
            popover => {
              const popStyle = getComputedStyle(popover);
              const pickerInnerWidth = popover.clientWidth
                - parseFloat(popStyle.paddingLeft) - parseFloat(popStyle.paddingRight);
              const mode = popover.querySelector('[data-testid="gfd-cp-mode"]').getBoundingClientRect();
              const grid = popover.querySelector('.gfd-cp-grid').getBoundingClientRect();
              const visual = popover.querySelector('.gfd-cp-visual').getBoundingClientRect();
              const previews = popover.querySelector('.gfd-cp-previews').getBoundingClientRect();
              const favourites = popover.querySelector('.gfd-cp-favs').getBoundingClientRect();
              const newLabel = popover.querySelector('.gfd-cp-new-label');
              const dropper = popover.querySelector('.gfd-cp-dropper');
              const cssValue = popover.querySelector('[data-testid="gfd-cp-hex"]');
              const previewLabels = [...popover.querySelectorAll('.gfd-cp-preview-label')]
                .map(label => label.textContent);
              const trackRows = [...popover.querySelectorAll('.gfd-cp-track-row')]
                .map(row => row.getBoundingClientRect());
              const previewElements = [...popover.querySelectorAll('.gfd-cp-preview')];
              const previewRows = previewElements.map(row => row.getBoundingClientRect());
              const verticalGaps = [
                ...trackRows.slice(1).map((row, index) => row.top - trackRows[index].bottom),
                previews.top - visual.bottom,
                previewRows[1].top - previewRows[0].bottom,
                favourites.top - previews.bottom,
              ];
              return {
                modeFirst: mode.bottom < grid.top,
                modeUsesPickerWidth: Math.abs(mode.width - pickerInnerWidth) <= 1,
                singleColumn: visual.bottom <= previews.top && previews.bottom <= favourites.top,
                noVerticalVoid: previews.top - visual.bottom <= 9,
                columnWidthsMatch: [visual, previews, favourites]
                  .every(item => Math.abs(item.width - grid.width) <= 1),
                hasNoNumericFields: !popover.querySelector('.gfd-cp-num, .gfd-cp-fields'),
                hasEvenVerticalSpacing: verticalGaps.every(gap => Math.abs(gap - 8) <= 1),
                currentComesFirst: previewLabels.join(',') === 'Current,New',
                valueIsInNewPreview: cssValue.closest('.gfd-cp-preview') === previewElements[1],
                previewsMatchWidth: Math.abs(previewRows[0].width - previewRows[1].width) <= 1,
                previewFontsAreNinePixels: previewElements.every(preview => {
                  const value = preview.matches('button') ? preview.firstElementChild : cssValue;
                  return getComputedStyle(value).fontSize === '9px';
                }),
                dropperSharesNewLabel: !dropper || dropper.parentElement === newLabel,
              };
            }
            """);
        Assert.True(pickerLayout.GetProperty("modeFirst").GetBoolean(), $"mode was not first: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("modeUsesPickerWidth").GetBoolean(),
            $"mode did not use the picker width: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("singleColumn").GetBoolean(),
            $"picker controls did not form one column: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("noVerticalVoid").GetBoolean(),
            $"picker left empty space below its visual controls: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("columnWidthsMatch").GetBoolean(),
            $"picker column widths did not match: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("hasNoNumericFields").GetBoolean(),
            $"numeric channel fields remained: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("hasEvenVerticalSpacing").GetBoolean(),
            $"picker row spacing was uneven: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("currentComesFirst").GetBoolean(),
            $"Current did not precede New: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("valueIsInNewPreview").GetBoolean(),
            $"CSS text did not move into the New preview: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("previewsMatchWidth").GetBoolean(),
            $"Current and New previews did not match widths: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("previewFontsAreNinePixels").GetBoolean(),
            $"preview values did not use the compact font size: {pickerLayout}");
        Assert.True(pickerLayout.GetProperty("dropperSharesNewLabel").GetBoolean(),
            $"eyedropper did not share the New label cell: {pickerLayout}");

        // It stays open through picking and redraws until it is dismissed: Escape closes it.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();

        await Assertions.Expect(clearFill).ToBeEnabledAsync();
        await fillControl.HoverAsync();
        await Assertions.Expect(clearFill).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(clearFill).ToHaveCSSAsync("border-top-width", "0px");
        await Assertions.Expect(clearFill).ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");

        // A formula's swatch reports the colour rather than taking one, so it is disabled.
        var swatch = page.GetByTestId("colour-swatch-color.light");
        await Assertions.Expect(swatch).ToBeDisabledAsync();
        await Assertions.Expect(swatch).ToHaveAttributeAsync("title",
            new System.Text.RegularExpressions.Regex("#3366ff"));

        // The swatch follows the box as it is typed in: it is the answer to the formula, and the
        // panel is not rebuilt on a keystroke because the cursor is in the box being typed into.
        await page.GetByTestId("colour-color.light").FillAsync("=concat(\"#\", \"aa2211\")");
        await Assertions.Expect(page.GetByTestId("colour-swatch-color.light"))
            .ToHaveCSSAsync("background-color", "rgb(170, 34, 17)");

        await page.GetByTestId("colour-color.light").FillAsync("=upper(\"not a colour\")");
        var crossed = page.GetByTestId("colour-bad-color.light");
        await Assertions.Expect(crossed).ToHaveTextAsync("✕");
        await Assertions.Expect(crossed).ToHaveAttributeAsync(
            "title", new System.Text.RegularExpressions.Regex("is not a colour"));

        // A variable is a colour the canvas bytes cannot read directly, but the cascade resolves
        // it: the picker shows what the variable really says instead of quietly starting at black.
        await page.GetByTestId("colour-fill.light")
            .FillAsync("var(--gridlet-test-absent, #224466)");
        var varSwatch = page.GetByTestId("colour-swatch-fill.light");
        await varSwatch.ClickAsync();
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("hex");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("#224466");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();

        // Transparency is a real colour, not the invalid marker: the picker opens at zero alpha
        // rather than pretending the value was opaque black.
        await page.GetByTestId("colour-fill.light").FillAsync("transparent");
        var transparentSwatch = page.GetByTestId("colour-swatch-fill.light");
        await transparentSwatch.ClickAsync();
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("#00000000");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();

        // A named colour is a colour: the picker resolves it and can spell it back in any form.
        await page.GetByTestId("colour-fill.light").FillAsync("rebeccapurple");
        var namedSwatch = page.GetByTestId("colour-swatch-fill.light");
        await namedSwatch.ClickAsync();
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("gfd-cp-mode")).ToHaveValueAsync("name");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("rebeccapurple");
        await popover.GetByRole(AriaRole.Option, new() { Name = "cyan", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("cyan");
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("hex");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("#00ffff");

        // The perceptual spaces CSS spells colours in are editable in their own numbers: pure red
        // reads as the oklch the colour itself is famous for.
        await page.GetByTestId("colour-fill.light").FillAsync("#ff0000");
        await namedSwatch.ClickAsync();
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("oklch");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex"))
            .ToHaveValueAsync("oklch(62.8% 0.258 29.2)");

        // The other perceptual spaces answer in their own ranges: CIE Lab and LCH keep their
        // numbers where CIE keeps them, far from the Ok variants' quarters, and a value typed in
        // CIE LCH is stored spelled in CIE LCH rather than mistaken for its Ok cousin.
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("lab");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex"))
            .ToHaveValueAsync("lab(54.3% 80.81 69.89)");
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("lch");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex"))
            .ToHaveValueAsync("lch(54.3% 106.84 40.9)");
        await page.GetByTestId("gfd-cp-hex").FillAsync("lch(54.3% 106.84 40.9)");
        await Assertions.Expect(page.GetByTestId("colour-fill.light"))
            .ToHaveValueAsync("lch(54.3% 106.84 40.9)");
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("oklab");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex"))
            .ToHaveValueAsync("oklab(62.8% 0.225 0.126)");

        // Canvas pixels come back premultiplied by alpha, so a barely-there colour must still read
        // as the colour it is: five-percent red keeps its channels when the picker opens.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();
        await page.GetByTestId("colour-fill.light").FillAsync("rgb(255 0 0 / 0.05)");
        await namedSwatch.ClickAsync();
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("rgb");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex"))
            .ToHaveValueAsync("rgb(255 0 0 / 0.05)");

        // A favourite preserves the mode and literal it was created in rather than flattening
        // every saved swatch to hex.
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("hsl");
        await Assertions.Expect(page.GetByTestId("gfd-cp-hex")).ToHaveValueAsync("hsl(0 100% 50% / 0.05)");
        await page.EvaluateAsync(
            """
            localStorage.setItem('gridlet.designer.colourFavourites', JSON.stringify([
              '#001122', '#112233', '#223344', '#334455', '#445566', '#556677', '#667788', '#778899',
              '#8899aa', '#99aabb', '#aabbcc', '#bbccdd', '#ccddee', '#ddeeff', '#123456', '#654321'
            ]))
            """);
        await popover.GetByRole(AriaRole.Button,
            new() { Name = "Save the new colour to favourites" }).ClickAsync();
        await Assertions.Expect(popover.Locator(".gfd-cp-fav").First).ToBeVisibleAsync();
        var favouritesLayout = await popover.EvaluateAsync<JsonElement>(
            """
            popover => {
              const palette = popover.querySelector('.gfd-cp-favs').getBoundingClientRect();
              const visual = popover.querySelector('.gfd-cp-visual').getBoundingClientRect();
              const swatches = [...popover.querySelectorAll('.gfd-cp-favs > *')]
                .map(swatch => swatch.getBoundingClientRect());
              return {
                staysInColumn: palette.right <= visual.right + 1 && palette.width <= visual.width + 1,
                wraps: swatches.at(-1).top > swatches[0].top,
              };
            }
            """);
        Assert.True(favouritesLayout.GetProperty("staysInColumn").GetBoolean(),
            $"favourites expanded the picker width: {favouritesLayout}");
        Assert.True(favouritesLayout.GetProperty("wraps").GetBoolean(),
            $"favourites did not wrap: {favouritesLayout}");
        await page.GetByTestId("gfd-cp-mode").SelectOptionAsync("hex");
        await page.GetByTestId("gfd-cp-hex").FillAsync("#123123");
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("#123123");
        await popover.Locator(".gfd-cp-fav").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("gfd-cp-mode")).ToHaveValueAsync("hsl");
        await Assertions.Expect(page.GetByTestId("colour-fill.light"))
            .ToHaveValueAsync("hsl(0 100% 50% / 0.05)");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();

        // The popover belongs to the panel that opened it: selecting something else closes it,
        // so it can never keep writing into a control that is no longer the subject.
        await varSwatch.ClickAsync();
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await Canvas(page, "otherLabel").ClickAsync();
        await Assertions.Expect(popover).ToBeHiddenAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A handler is a formula run for what it does. It runs when the component runs, and not while
    /// somebody is still drawing the component.
    /// </summary>
    [Fact]
    public async Task Runs_a_handler_in_preview_and_leaves_it_alone_while_designing()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        // A handler's function is called like any other, with what it needs passed to it. `component` is
        // an ordinary name in a formula, so the same function works from any control.
        await WriteModuleAsync(page, "handlers.js", """
            export function announce(component, what) {
              component.field('output').value = 'clicked ' + what;
            }

            export function started(component) {
              component.field('output').value = 'loaded';
            }
            """);

        page = await OpenComponentAsync(browserPage, "Handler component",
        [
            Control("go", "button", props: new { text = "Go" },
                events: new { click = "=announce(component, \"once\")" }, y: 10),
            Control("output", "label", props: new { text = "waiting" }, y: 50),
        ],
            componentEvents: new { load = "=started(component)" },
            modules: ["handlers.js"]);

        // Design is for drawing the component. A click here selects a control; it does not run it.
        await Box(page, "go").ClickAsync();
        await Assertions.Expect(Canvas(page, "output")).ToHaveTextAsync("waiting");

        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(Canvas(page, "output")).ToHaveTextAsync("loaded");

        await Canvas(page, "go").ClickAsync();
        await Assertions.Expect(Canvas(page, "output")).ToHaveTextAsync("clicked once");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The two ways a handler fails to do anything: it was never a formula, so it could not run at
    /// all; or it ran and the formula failed.
    /// </summary>
    [Fact]
    public async Task Reports_a_handler_that_cannot_run()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Broken handler component",
            [Control("go", "button", props: new { text = "Go" },
                events: new { click = "announce(1)" })]);

        await Box(page, "go").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        var handler = page.GetByTestId("event-click");
        await Assertions.Expect(handler)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bad"));
        await Assertions.Expect(handler)
            .ToHaveAttributeAsync("title", "A handler has to start with = to run.");

        // Made into a formula, it can run - and now it is the formula that fails, which is said
        // beside the control that holds it rather than swallowed.
        await handler.FillAsync("=nosuch(1)");
        await page.GetByTestId("component-view-preview").ClickAsync();
        await Canvas(page, "go").ClickAsync();
        await Assertions.Expect(page.Locator(".gfd-code-problem"))
            .ToContainTextAsync("click: There is no function called \"nosuch\".");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The class and the id belong to the control, not to the box the designer positions it with.
    /// A rule written against them has to style the thing it names.
    /// </summary>
    [Fact]
    public async Task Puts_the_class_and_the_id_on_the_control_rather_than_on_its_box()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Styling component",
            [Control("go", "button", props: new { text = "Go" })]);

        await Box(page, "go").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("expr-classes").FillAsync("btn wide");
        await page.GetByTestId("expr-elementId").FillAsync("goButton");

        var box = Box(page, "go");
        var button = Canvas(page, "go");

        // The name, the class and the id are all on the button: "go" is the button to whoever
        // named it, and a rule written against any of the three styles what it looks like it does.
        await Assertions.Expect(button).ToHaveJSPropertyAsync("tagName", "BUTTON");
        await Assertions.Expect(button).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bbtn\b.*\bwide\b|\bwide\b.*\bbtn\b"));
        await Assertions.Expect(button).ToHaveAttributeAsync("id", "goButton");

        // The box keeps the designer's own classes and none of the operator's, and it carries the
        // name under its own attribute so one rule can never mean both elements.
        await Assertions.Expect(box).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"^gfd-control\b"));
        await Assertions.Expect(box).Not.ToHaveAttributeAsync("id", "goButton");
        await Assertions.Expect(box).Not.ToHaveAttributeAsync("data-name", "go");
        await Assertions.Expect(box).ToHaveAttributeAsync("data-control-box", "go");
        await Assertions.Expect(box.Locator("[data-name='go']")).ToHaveCountAsync(1);

        // Both stylesheets written the obvious way land on the button.
        await page.EvaluateAsync(
            "() => document.head.append(Object.assign(document.createElement('style'), " +
            "{ textContent: '.btn { border-width: 7px; border-style: solid; } " +
            "[data-name=\"go\"] { outline-width: 3px; outline-style: solid; }' }))");
        Assert.Equal("7px", await button.EvaluateAsync<string>(
            "element => getComputedStyle(element).borderTopWidth"));
        Assert.Equal("3px", await button.EvaluateAsync<string>(
            "element => getComputedStyle(element).outlineWidth"));

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Isolating a component starts it from the browser's own styling. It does not start it from the
    /// browser's own styling and then keep it there: the reset that does the isolating has to lose
    /// to the CSS written on top of it, or asking for a clean slate would mean losing your own work.
    /// </summary>
    [Fact]
    public async Task Lets_a_plain_class_style_a_control_on_an_isolated_component()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Isolated component",
            [Control("go", "button", props: new { text = "Go" })],
            isolated: true,
            css: ".btn { background: rgba(228,228,228); border: 1px solid red; border-radius: 5px; }");

        await Box(page, "go").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("expr-classes").FillAsync("btn");

        var button = Canvas(page, "go");
        var computed = async (string property) => await button.EvaluateAsync<string>(
            $"element => getComputedStyle(element).{property}");

        Assert.Equal("rgb(255, 0, 0)", await computed("borderTopColor"));
        Assert.Equal("5px", await computed("borderTopLeftRadius"));
        Assert.Equal("rgb(228, 228, 228)", await computed("backgroundColor"));

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Every control kind the palette offers still builds, and a tick-box property still works from
    /// the tick as well as from a formula. The pager is the one that would notice: both of its
    /// properties are booleans, and both change what it draws.
    /// </summary>
    [Fact]
    public async Task Keeps_every_control_kind_and_its_tick_box_properties_working()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Palette component",
        [
            Control("aLabel", "label", y: 10),
            Control("aTextbox", "textbox", y: 40),
            Control("aTextarea", "textarea", y: 70),
            Control("aCheckbox", "checkbox", y: 100),
            Control("aSelect", "select", y: 130),
            Control("aButton", "button", y: 160),
            Control("aPanel", "panel", y: 190),
            Control("pages", "pager", props: new { edges = true, position = true }, y: 300),
        ]);

        // One palette button per kind somebody can add. Multi-line is not one of them: the text box
        // does that now, and the kind survives only so documents that used it keep loading - which
        // is why the seeded textarea below still has to draw.
        await Assertions.Expect(page.Locator(".gfd-palette button")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator(".gfd-palette [data-type='textarea']")).ToHaveCountAsync(0);
        foreach (var name in new[]
        {
            "aLabel", "aTextbox", "aTextarea", "aCheckbox", "aSelect", "aButton", "aPanel", "pages",
        })
        {
            await Assertions.Expect(Canvas(page, name)).ToBeVisibleAsync();
        }

        // Both ends and the position: five buttons' worth of pager.
        var pager = Canvas(page, "pages");
        await Assertions.Expect(pager.Locator(".gfd-pager-btn")).ToHaveCountAsync(4);
        await Assertions.Expect(pager.Locator(".gfd-pager-position")).ToHaveCountAsync(1);

        await pager.ClickAsync();
        await OpenPanelTabAsync(page, "Settings");

        // The tick writes into the box beside it, and the box is what the property reads.
        await page.Locator("[data-property='edges'] .gfd-check").UncheckAsync();
        await Assertions.Expect(page.GetByTestId("expr-edges")).ToHaveValueAsync("false");
        await Assertions.Expect(Canvas(page, "pages").Locator(".gfd-pager-btn")).ToHaveCountAsync(2);

        // And a formula decides it just as well, which is what the tick could never express.
        await page.GetByTestId("expr-position").FillAsync("=component.rowCount > 1000");
        await Assertions.Expect(Canvas(page, "pages").Locator(".gfd-pager-position"))
            .ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-property='position'] .gfd-check"))
            .ToBeDisabledAsync();

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task An_isolated_checkbox_keeps_its_label_and_input_structure()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Isolated checkbox component",
            [Control("consent", "checkbox", props: new { text = "Accept terms" }, w: 220)],
            isolated: true);

        var checkbox = Canvas(page, "consent");
        await Assertions.Expect(checkbox).ToHaveCSSAsync("display", "flex");
        await Assertions.Expect(checkbox.Locator("input")).ToHaveCSSAsync("width", "13px");
        await Assertions.Expect(checkbox.Locator("span")).ToHaveTextAsync("Accept terms");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// An unsaved component is only at risk when its tab closes. Switching away leaves it open with the
    /// edit still in it, and a component and the module it runs are two tabs somebody moves between
    /// constantly, so asking on the way is asking about nothing.
    /// </summary>
    [Fact]
    public async Task Asks_about_unsaved_work_when_the_tab_closes_and_not_when_it_is_left()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Leaving component",
            [Control("caption", "label", props: new { text = "literal" })]);

        await Canvas(page, "caption").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("expr-text").FillAsync("Edited, not saved");
        await Assertions.Expect(page.GetByTestId("component-save")).ToBeEnabledAsync();

        // Somewhere else in the workspace, and back: no question either way.
        await page.Locator("#ask-btn").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToHaveCountAsync(0);
        await page.Locator(".tab-title").Filter(
            new LocatorFilterOptions { HasTextString = "Leaving component" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("expr-text")).ToHaveValueAsync("Edited, not saved");

        // Closing it is what loses the work, so that is the moment worth a question.
        await page.Locator(".tab.active .tab-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToContainTextAsync("has unsaved changes");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Unsaved work is marked on the tab itself, as a dot where the close button is. Resting on the
    /// tab turns it back into the ×, so the mark costs the row no width and closing never moves.
    /// </summary>
    [Fact]
    public async Task Marks_a_tab_with_unsaved_work_and_gives_the_close_button_back_on_hover()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Marked component",
            [Control("caption", "label", props: new { text = "literal" })]);

        var close = page.Locator(".tab.active .tab-close");
        var dot = close.Locator(".tab-close-mark");
        var cross = close.Locator(".tab-close-x");

        // Nothing is unsaved yet, so it is an ordinary close button.
        await Assertions.Expect(close).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bunsaved\b"));
        await Assertions.Expect(cross).ToBeVisibleAsync();
        await Assertions.Expect(dot).ToBeHiddenAsync();

        await Canvas(page, "caption").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await page.GetByTestId("expr-text").FillAsync("Edited");

        // The mark appears as soon as the component becomes dirty, without anything else redrawing.
        await Assertions.Expect(page.GetByTestId("tab-unsaved")).ToBeVisibleAsync();
        await Assertions.Expect(dot).ToBeVisibleAsync();
        await Assertions.Expect(cross).ToBeHiddenAsync();
        await Assertions.Expect(close).ToHaveAttributeAsync(
            "title", "Unsaved changes - click to close tab");

        // Reaching for the tab is reaching for its close button.
        await page.Locator(".tab.active").HoverAsync();
        await Assertions.Expect(cross).ToBeVisibleAsync();
        await Assertions.Expect(dot).ToBeHiddenAsync();

        // Saving takes the mark away again.
        await page.GetByTestId("component-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("tab-unsaved")).ToHaveCountAsync(0);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A binding used to be an expression on its own, told apart from a value by the field it lived
    /// in. A component saved that way still opens, and its expressions are given the <c>=</c> they
    /// always meant.
    /// </summary>
    [Fact]
    public async Task Upgrades_an_older_document_s_expressions_to_formulas()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Older component",
            [Control("caption", "label", bind: new { text = "upper(\"still works\")" })]);

        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("STILL WORKS");

        await Canvas(page, "caption").ClickAsync();
        await OpenPanelTabAsync(page, "Settings");
        await Assertions.Expect(page.GetByTestId("expr-text"))
            .ToHaveValueAsync("=upper(\"still works\")");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid shows a collection rather than a place in one, so it draws its columns and, while the
    /// component is being laid out, a placeholder row: a laid-out component reads as a description
    /// of what it will show rather than as an empty box.
    /// </summary>
    [Fact]
    public async Task A_data_grid_draws_the_columns_the_document_names()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Grid component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 400, h: 160)]);

        var grid = Canvas(page, "rows");
        await Assertions.Expect(grid.Locator("thead th")).ToHaveCountAsync(2);
        await Assertions.Expect(grid.Locator("thead th").First).ToHaveTextAsync("Name");
        await Assertions.Expect(grid.Locator("tbody td").First).ToHaveTextAsync("[Name]");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The document is the component, so Code shows the file itself rather than a rendering of it.
    /// </summary>
    [Fact]
    public async Task Code_shows_the_document_the_component_is_stored_as()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Readable component",
            [Control("caption", "label", props: new { text = "Hello" })]);

        await page.GetByTestId("component-view-code").ClickAsync();
        var document = await page.GetByTestId("component-document-editor").InputValueAsync();

        Assert.Contains(@"data-gridlet=""2""", document, StringComparison.Ordinal);
        Assert.Contains(@"<span data-name=""caption""", document, StringComparison.Ordinal);
        Assert.Contains(">Hello</span>", document, StringComparison.Ordinal);

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Editing the document edits the component. This is the whole point of storing HTML: there is
    /// nothing between what is typed here and what the component is.
    /// </summary>
    [Fact]
    public async Task Editing_the_document_changes_the_component()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Editable component",
            [Control("caption", "label", props: new { text = "Before" })]);

        await page.GetByTestId("component-view-code").ClickAsync();
        var editor = page.GetByTestId("component-document-editor");
        var document = await editor.InputValueAsync();
        await editor.FillAsync(document.Replace(">Before<", ">After<", StringComparison.Ordinal));

        await page.GetByTestId("component-view-design").ClickAsync();
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("After");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Half-typed markup is not a component yet. It is reported where it is being typed, and the
    /// component keeps the last document that did parse - the same bargain a broken formula makes.
    /// </summary>
    [Fact]
    public async Task Markup_that_is_not_a_document_is_reported_and_changes_nothing()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Broken edit component",
            [Control("caption", "label", props: new { text = "Intact" })]);

        await page.GetByTestId("component-view-code").ClickAsync();
        await page.GetByTestId("component-document-editor").FillAsync("<p>not a component</p>");

        await Assertions.Expect(page.GetByTestId("component-code-error")).ToBeVisibleAsync();

        await page.GetByTestId("component-view-design").ClickAsync();
        await Assertions.Expect(Canvas(page, "caption")).ToHaveTextAsync("Intact");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid bound to a published endpoint shows what the endpoint returned, drawn by the
    /// workspace's own grid. The source is named by its route, which is what the document stores
    /// and what the component calls, so it keeps meaning the same endpoint in another environment.
    /// </summary>
    /// <remarks>
    /// Naming no columns is the case worth testing: the grid takes the columns the source returned,
    /// so binding a grid to an endpoint shows its data without the document having to describe it
    /// first - and what arrives is what the endpoint really answered rather than a shape the test
    /// asked for.
    /// </remarks>
    [Fact]
    public async Task A_data_grid_shows_the_rows_its_source_returned()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var route = $"grid-rows-{Guid.NewGuid():n}";
        var published = await page.APIRequest.PostAsync("/gridlet/api/published", new APIRequestContextOptions
        {
            DataObject = new
            {
                name = "Grid rows",
                method = "GET",
                route,
                connectionName = "Main",
                sql = "SELECT 1",
                parameters = Array.Empty<object>(),
                enabled = true,
            },
        });
        Assert.True(published.Ok, $"Publishing the endpoint failed: {published.Status}");

        page = await OpenComponentAsync(browserPage, "Bound grid component",
            [Control("answers", "grid", props: new { columns = "", header = true }, w: 400, h: 160)],
            source: route);

        // The pager reports where the component is in its rows, so it says whether the source
        // answered at all before the grid is read for what it answered with.
        await page.GetByTestId("component-view-preview").ClickAsync();
        await Assertions.Expect(page.GetByTestId("component-record-position")).ToContainTextAsync("of 1");

        var grid = Canvas(page, "answers");
        await Assertions.Expect(grid.Locator("thead th")).ToHaveCountAsync(1);
        await Assertions.Expect(grid.Locator("thead th").First).ToContainTextAsync("Answer");
        await Assertions.Expect(grid.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(grid.Locator("tbody td").First).ToHaveTextAsync("42");

        // Drawn by the workspace's grid rather than one of the designer's own making, so a row in a
        // component looks like a row anywhere else in the workspace.
        await Assertions.Expect(grid).ToHaveClassAsync(new Regex("data-grid"));

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// The document editor offers what the document can say, the same way the stylesheet editor
    /// offers what a rule can say. The offers are read off the catalogue, so a kind that gains a
    /// property gains it here too.
    /// </summary>
    [Fact]
    public async Task Suggests_what_an_element_in_the_document_can_be_told()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Completing document",
            [Control("go", "button", props: new { text = "Go" })]);

        await page.GetByTestId("component-view-code").ClickAsync();
        var editor = page.GetByTestId("component-document-editor");
        var items = page.GetByTestId("html-completions").Locator(".gfd-complete-item");

        await editor.PressAsync("Control+End");
        await editor.PressSequentiallyAsync("<butt");
        await Assertions.Expect(items.First).ToContainTextAsync("button");

        await editor.PressAsync("Enter");
        await editor.PressSequentiallyAsync(" data-on-cl");
        await Assertions.Expect(items.First).ToContainTextAsync("data-on-click");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid stays inside the box it was placed in, however many rows arrive, and scrolls instead
    /// of spilling over the controls around it.
    /// </summary>
    /// <remarks>
    /// Isolated is the case that broke: the scroll container used to live in the kind's appearance,
    /// and isolating once meant dropping that appearance, so the grid lost the box it was placed in
    /// along with the styling. Staying where it was put is structure, not styling. An isolated
    /// component now takes the basic style like any other styling root, so its cells are the same
    /// cells - the point of the test is the box, and that it still has to scroll to fit.
    /// </remarks>
    [Fact]
    public async Task A_data_grid_stays_inside_the_box_it_was_placed_in()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Grid bounds component",
            [Control("rows", "grid", props: new { columns = "A\nB\nC\nD\nE\nF\nG\nH", header = true },
                x: 20, y: 20, w: 200, h: 60)],
            isolated: true);

        var box = Canvas(page, "rows");

        // Measured against the geometry the document asked for, which is the promise a placed
        // control makes to everything laid out around it.
        var size = await box.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            (grid) => {
              const box = grid.closest('.gfd-control').getBoundingClientRect();
              const viewport = grid.closest('.gfd-grid-viewport');
              const rect = viewport.getBoundingClientRect();
              return {
                width: Math.round(rect.width),
                height: Math.round(rect.height),
                overflowsBox: rect.bottom > box.bottom + 1 || rect.right > box.right + 1,
                scrolls: viewport.scrollHeight > viewport.clientHeight
                  || viewport.scrollWidth > viewport.clientWidth,
              };
            }
            """);

        Assert.Equal(200, size.GetProperty("width").GetInt32());
        Assert.Equal(60, size.GetProperty("height").GetInt32());
        Assert.False(size.GetProperty("overflowsBox").GetBoolean(), "the grid spilled outside its box");

        // Eight columns and a header cannot fit a 200x60 box, so it has to be scrolling to fit.
        Assert.True(size.GetProperty("scrolls").GetBoolean(), "the grid did not scroll its content");
        await Assertions.Expect(box.Locator("thead th").First).ToHaveCSSAsync("border-bottom-width", "1px");
        await Assertions.Expect(box.Locator("thead th").First).ToHaveCSSAsync("padding", "3px 7px");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A grid's appearance belongs to the grid rather than falling back to the component's text and
    /// fill when the grid is rendered by the designer.
    /// </summary>
    [Fact]
    public async Task A_data_grid_uses_its_own_text_and_fill_colours()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Grid colour component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 240, h: 80)]);

        var theme = page.GetByTestId("component-theme");
        for (var attempts = 0; attempts < 3 && await theme.GetAttributeAsync("data-theme-mode") != "light"; attempts++)
        {
            await theme.ClickAsync();
        }

        await Canvas(page, "rows").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await page.GetByTestId("colour-color.light").FillAsync("rgb(1 2 3)");
        await page.GetByTestId("colour-fill.light").FillAsync("rgb(4 5 6)");
        await page.EvaluateAsync(
            """
            () => {
              const style = document.createElement('style');
              style.textContent = 'table.grid { color: rgb(9, 9, 9); background-color: rgb(9, 9, 9); }';
              document.head.append(style);
            }
            """);

        await Assertions.Expect(Canvas(page, "rows")).ToHaveCSSAsync("color", "rgb(1, 2, 3)");
        await Assertions.Expect(Canvas(page, "rows"))
            .ToHaveCSSAsync("background-color", "rgb(4, 5, 6)");
        var grid = Canvas(page, "rows");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("color", "rgb(1, 2, 3)");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("background-color", "rgb(4, 5, 6)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("color", "rgb(1, 2, 3)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("background-color", "rgb(4, 5, 6)");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>The same grid appearance must work when the workspace is using its dark palette.</summary>
    [Fact]
    public async Task A_data_grid_uses_its_own_dark_theme_text_and_fill_colours()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Dark grid colour component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 240, h: 80)]);

        var theme = page.GetByTestId("component-theme");
        for (var attempts = 0; attempts < 3 && await theme.GetAttributeAsync("data-theme-mode") != "dark"; attempts++)
        {
            await theme.ClickAsync();
        }

        await Canvas(page, "rows").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await page.GetByTestId("colour-color.dark").FillAsync("#ff0000");
        await page.GetByTestId("colour-fill.dark").FillAsync("#000000");

        await Assertions.Expect(Canvas(page, "rows")).ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(Canvas(page, "rows"))
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");
        var grid = Canvas(page, "rows");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>Following a dark workspace must select the grid's dark appearance too.</summary>
    [Fact]
    public async Task A_data_grid_uses_dark_colours_when_its_theme_follows_the_workspace()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Auto dark grid colour component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 240, h: 80)]);

        await page.EvaluateAsync("document.documentElement.dataset.theme = 'dark'");
        var theme = page.GetByTestId("component-theme");
        for (var attempts = 0; attempts < 3 && await theme.GetAttributeAsync("data-theme-mode") != "auto"; attempts++)
        {
            await theme.ClickAsync();
        }

        await Canvas(page, "rows").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");
        await page.GetByTestId("colour-color.dark").FillAsync("#ff0000");
        await page.GetByTestId("colour-fill.dark").FillAsync("#000000");

        await Assertions.Expect(Canvas(page, "rows")).ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(Canvas(page, "rows"))
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");
        var grid = Canvas(page, "rows");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(grid.Locator("thead th").First)
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await Assertions.Expect(grid.Locator("tbody td").First)
            .ToHaveCSSAsync("background-color", "rgb(0, 0, 0)");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Resizing a preview grid may make its table wider, but the positioned control remains the
    /// chosen size and its inner viewport owns the horizontal scrollbar.
    /// </summary>
    [Fact]
    public async Task A_preview_data_grid_contains_resized_columns_inside_its_box()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Resizable grid component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 200, h: 80)]);

        await page.GetByTestId("component-view-preview").ClickAsync();
        var grid = Canvas(page, "rows");
        await Assertions.Expect(grid.Locator(".col-grip")).ToHaveCountAsync(2);
        await DragGripByAsync(page, grid.Locator(".col-grip").First, 160, 0);

        var bounds = await page.EvaluateAsync<JsonElement>("""
            () => {
              const grid = document.querySelector('[data-name="rows"]');
              const box = grid.closest('.gfd-control');
              const viewport = grid.closest('.gfd-grid-viewport');
              return {
                boxWidth: Math.round(box.getBoundingClientRect().width),
                viewportWidth: Math.round(viewport.getBoundingClientRect().width),
                gridWidth: Math.round(grid.getBoundingClientRect().width),
                scrolls: viewport.scrollWidth > viewport.clientWidth,
              };
            }
            """);

        Assert.Equal(200, bounds.GetProperty("boxWidth").GetInt32());
        Assert.Equal(200, bounds.GetProperty("viewportWidth").GetInt32());
        Assert.True(bounds.GetProperty("gridWidth").GetInt32() > 200,
            $"the resized column did not widen the table: {bounds}");
        Assert.True(bounds.GetProperty("scrolls").GetBoolean(),
            $"the control did not contain the widened table: {bounds}");

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Both scroll directions belong to the fixed viewport, so the vertical scrollbar stays at the
    /// component's right edge even when a resized column makes the table wider than the viewport.
    /// </summary>
    [Fact]
    public async Task A_data_grid_keeps_both_scrollbars_at_the_component_edges()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = await OpenComponentAsync(browserPage, "Two-axis grid component",
            [Control("rows", "grid", props: new { columns = "Name\nCity", header = true }, w: 200, h: 80)]);

        await page.GetByTestId("component-view-preview").ClickAsync();
        var grid = Canvas(page, "rows");

        // Preview data normally comes from the source. Adding rows here keeps this layout test
        // independent of the provider's result size while exercising the same rendered table.
        await grid.EvaluateAsync(
            """
            grid => {
              const tbody = grid.tBodies[0];
              for (let index = 0; index < 30; index += 1) {
                const row = tbody.insertRow();
                row.insertCell().textContent = `Name ${index}`;
                row.insertCell().textContent = `City ${index}`;
              }
            }
            """);
        await DragGripByAsync(page, grid.Locator(".col-grip").First, 160, 0);

        var bounds = await page.EvaluateAsync<JsonElement>("""
            () => {
              const grid = document.querySelector('[data-name="rows"]');
              const box = grid.closest('.gfd-control');
              const viewport = grid.closest('.gfd-grid-viewport');
              const boxRect = box.getBoundingClientRect();
              const viewportRect = viewport.getBoundingClientRect();
              return {
                rightAtBoxEdge: Math.abs(viewportRect.right - boxRect.right) <= 1,
                overflowY: getComputedStyle(viewport).overflowY,
                scrollsVertically: viewport.scrollHeight > viewport.clientHeight,
                scrollsHorizontally: viewport.scrollWidth > viewport.clientWidth,
              };
            }
            """);

        Assert.True(bounds.GetProperty("rightAtBoxEdge").GetBoolean(),
            $"the vertical scrollbar's viewport moved with the table: {bounds}");
        Assert.Equal("auto", bounds.GetProperty("overflowY").GetString());
        Assert.True(bounds.GetProperty("scrollsVertically").GetBoolean(),
            $"the grid did not expose vertical scrolling: {bounds}");
        Assert.True(bounds.GetProperty("scrollsHorizontally").GetBoolean(),
            $"the grid did not expose horizontal scrolling: {bounds}");

        browserPage.AssertNoUnexpectedErrors();
    }
}
