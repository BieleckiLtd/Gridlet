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
            // A component names the modules it runs, and may name which of a module's classes it
            // runs with it. A bare file name is the common case and stays a bare file name.
            if (module is string file)
            {
                body.Add($"<link rel=\"gridlet-module\" href=\"{Escape(file)}\">");
                continue;
            }

            var entry = Values(module);
            var href = entry.TryGetValue("module", out var moduleFile) ? moduleFile : string.Empty;
            var className = entry.TryGetValue("class", out var value) ? value : null;
            body.Add(className is null
                ? $"<link rel=\"gridlet-module\" href=\"{Escape(href)}\">"
                : $"<link rel=\"gridlet-module\" href=\"{Escape(href)}\" data-class=\"{Escape(className)}\">");
        }

        if (!string.IsNullOrEmpty(css))
        {
            body.Add($"<style>{css}</style>");
        }

        body.AddRange(controls);

        var html = $"<form {string.Join(" ", attributes)}>{string.Join("", body)}</form>";

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

        await Canvas(page, "swatchLabel").ClickAsync();
        await OpenPanelTabAsync(page, "Appearance");

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
        var document = await page.GetByTestId("component-code-editor").InputValueAsync();

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
        var editor = page.GetByTestId("component-code-editor");
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
        await page.GetByTestId("component-code-editor").FillAsync("<p>not a component</p>");

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
        var editor = page.GetByTestId("component-code-editor");
        var items = page.GetByTestId("html-completions").Locator(".gfd-complete-item");

        await editor.PressAsync("Control+End");
        await editor.PressSequentiallyAsync("<butt");
        await Assertions.Expect(items.First).ToContainTextAsync("button");

        await editor.PressAsync("Enter");
        await editor.PressSequentiallyAsync(" data-on-cl");
        await Assertions.Expect(items.First).ToContainTextAsync("data-on-click");

        browserPage.AssertNoUnexpectedErrors();
    }
}
