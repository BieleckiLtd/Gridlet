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
        int h = 24)
    {
        var properties = Values(props ?? new { text = "literal" });
        var attributes = new List<string>
        {
            $"data-name=\"{Escape(name)}\"",
            $"style=\"left: {x}px; top: {y}px; width: {w}px; height: {h}px;\"",
        };

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

    /// <summary>A grid's columns, which are its header — the rows are the source's, not the document's.</summary>
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
        string? source = null)
    {
        var page = browserPage.Page;

        var attributes = new List<string>
        {
            @"data-gridlet=""2""",
            $"data-name=\"{Escape(name)}\"",
            @"data-layout=""free""",
            @"style=""width: 720px; height: 460px;""",
        };

        if (isolated)
        {
            attributes.Add("data-isolated");
        }

        if (resizable)
        {
            attributes.Add("data-resizable");
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
            DataObject = new { name, html },
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
        await page.Locator($"button.tree-item[title^='{name} —']").ClickAsync();
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

    /// <summary>The control itself — the button, the input — which is what carries its name.</summary>
    private static ILocator Canvas(IPage page, string name) => page.Locator($"[data-name='{name}']");

    /// <summary>The box the designer positions the control with.</summary>
    private static ILocator Box(IPage page, string name) => page.Locator($"[data-control-box='{name}']");

    private static Task OpenPanelTabAsync(IPage page, string tab) =>
        page.Locator($".gfd-tabs button[title='{tab}']").ClickAsync();

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
    /// edge to whatever happens to be within reach of it — and the frame's own edge is within
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
    /// Where a control's box sits inside the component, in the component's own pixels — read off
    /// the box rather than out of the document, so it is what the designer actually drew.
    /// </summary>
    private static Task<double> OffsetAsync(IPage page, string name, string edge) =>
        Box(page, name).EvaluateAsync<double>(
            $"element => parseFloat(getComputedStyle(element).{edge})");

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
            "total — is defined more than once",
            "LIMIT — is defined more than once",
        });

        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A control keeps its own name. A module's file stem is a qualifier for calls, and the two
    /// live in different places in an expression — <c>duty.w</c> is a path to the control, and
    /// <c>duty.vat(1)</c> is a call into the file — so neither has to lose.
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
    /// could come next. The box and the tab are two views of one string — what is typed in either
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
    /// What could come next, offered where the caret is: this component's own selectors, then the
    /// properties CSS has, then what the property being written takes. The list suggests and never
    /// decides — nothing arrives without being chosen.
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
            [Control("swatchLabel", "label", bind: new Dictionary<string, string>
            {
                ["color.light"] = "=concat(\"#\", \"3366ff\")",
            })]);

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

        // The slash is only the undefined marker; the swatch remains a working colour picker.
        var undefinedPicker = page.GetByTestId("colour-picker-fill.light");
        await Assertions.Expect(undefinedPicker).ToBeEnabledAsync();
        await undefinedPicker.EvaluateAsync("""
            picker => {
              picker.value = '#224466';
              picker.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await Assertions.Expect(page.GetByTestId("colour-fill.light")).ToHaveValueAsync("#224466");
        await Assertions.Expect(clearFill).ToBeEnabledAsync();
        await fillControl.HoverAsync();
        await Assertions.Expect(clearFill).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(clearFill).ToHaveCSSAsync("border-top-width", "0px");
        await Assertions.Expect(clearFill).ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");

        // The picker reports the colour rather than taking one: dragging it would be overwritten
        // on the next draw.
        var swatch = page.GetByTestId("colour-swatch-color.light");
        await Assertions.Expect(swatch).ToHaveValueAsync("#3366ff");
        await Assertions.Expect(swatch).ToBeDisabledAsync();

        // The swatch follows the box as it is typed in: it is the answer to the formula, and the
        // panel is not rebuilt on a keystroke because the cursor is in the box being typed into.
        await page.GetByTestId("colour-color.light").FillAsync("=concat(\"#\", \"aa2211\")");
        await Assertions.Expect(page.GetByTestId("colour-swatch-color.light"))
            .ToHaveValueAsync("#aa2211");

        await page.GetByTestId("colour-color.light").FillAsync("=upper(\"not a colour\")");
        var crossed = page.GetByTestId("colour-bad-color.light");
        await Assertions.Expect(crossed).ToHaveTextAsync("✕");
        await Assertions.Expect(crossed).ToHaveAttributeAsync(
            "title", new System.Text.RegularExpressions.Regex("is not a colour"));

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

        // Made into a formula, it can run — and now it is the formula that fails, which is said
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
        // does that now, and the kind survives only so documents that used it keep loading — which
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
            "title", "Unsaved changes — click to close tab");

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
    /// component keeps the last document that did parse — the same bargain a broken formula makes.
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
    /// first — and what arrives is what the endpoint really answered rather than a shape the test
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
    /// and an isolated component drops Gridlet's appearance on purpose, so the grid lost the box it
    /// was placed in along with the styling. Staying where it was put is structure, not styling.
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
              const rect = grid.getBoundingClientRect();
              return {
                width: Math.round(rect.width),
                height: Math.round(rect.height),
                overflowsBox: rect.bottom > box.bottom + 1 || rect.right > box.right + 1,
                scrolls: grid.scrollHeight > grid.clientHeight || grid.scrollWidth > grid.clientWidth,
              };
            }
            """);

        Assert.Equal(200, size.GetProperty("width").GetInt32());
        Assert.Equal(60, size.GetProperty("height").GetInt32());
        Assert.False(size.GetProperty("overflowsBox").GetBoolean(), "the grid spilled outside its box");

        // Eight columns and a header cannot fit a 200x60 box, so it has to be scrolling to fit.
        Assert.True(size.GetProperty("scrolls").GetBoolean(), "the grid did not scroll its content");

        browserPage.AssertNoUnexpectedErrors();
    }
}
