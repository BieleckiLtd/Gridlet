using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Gridlet.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class GridletUiTests(BrowserAppFixture fixture)
{
    [Fact]
    public async Task Theme_follows_system_and_persists_an_explicit_choice()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });

        await page.GotoAsync("/gridlet/");

        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        var themeButton = page.Locator("#theme-btn");
        await Assertions.Expect(themeButton).ToHaveAttributeAsync("aria-label", "Switch to dark theme");
        await Assertions.Expect(page.Locator("#topbar").GetByRole(
            AriaRole.Button, new() { Name = "More app actions" })).ToBeHiddenAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const children = [...document.querySelector('#topbar').children];
                return children.indexOf(document.querySelector('[data-overflow-for="theme-btn"]'))
                    < children.indexOf(document.querySelector('[data-overflow-for="apis-btn"]'));
            }
            """));

        if (!await themeButton.IsVisibleAsync())
        {
            await page.Locator("#topbar").GetByRole(
                AriaRole.Button, new() { Name = "More app actions" }).ClickAsync();
        }
        await themeButton.ClickAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Boots_and_streams_table_data()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;

        await page.GotoAsync("/gridlet/");

        await Assertions.Expect(page.Locator("#connection-select")).ToHaveValueAsync("Main");
        await Assertions.Expect(page.Locator("#database-select")).ToHaveValueAsync("FakeDb");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const sidebar = document.querySelector('#sidebar').getBoundingClientRect();
                const content = document.querySelector('#content').getBoundingClientRect();
                const grip = document.querySelector('#sidebar-grip').getBoundingClientRect();
                return Math.abs(sidebar.right - content.left) < 0.5
                    && grip.left < sidebar.right
                    && grip.right > content.left;
            }
            """));

        var customers = page.GetByTitle("dbo.Customers");
        await Assertions.Expect(customers).ToBeVisibleAsync();
        await customers.ClickAsync();

        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Columnheader, new() { Name = "Id int" })).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Columnheader, new() { Name = "Name nvarchar(100)" })).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Cell, new() { Name = "Ada" })).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Cell, new() { Name = "Grace" })).ToBeVisibleAsync();
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Runs_a_query_and_exports_exact_csv_and_json()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");

        await page.GetByTestId("query-run").ClickAsync();

        var results = page.GetByTestId("query-results");
        await Assertions.Expect(results.GetByRole(AriaRole.Cell, new() { Name = "42" })).ToBeVisibleAsync();
        await Assertions.Expect(results.GetByText("hello from fake", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");

        var csvDownload = await page.RunAndWaitForDownloadAsync(
            () => page.GetByTestId("export-csv").ClickAsync());
        Assert.Equal("SQL_1-result1.csv", csvDownload.SuggestedFilename);
        Assert.Equal("Answer\r\n42", await ReadDownloadAsync(csvDownload));

        var jsonDownload = await page.RunAndWaitForDownloadAsync(
            () => page.GetByTestId("export-json").ClickAsync());
        Assert.Equal("SQL_1-result1.json", jsonDownload.SuggestedFilename);
        using var document = JsonDocument.Parse(await ReadDownloadAsync(jsonDownload));
        Assert.Equal(42, document.RootElement[0].GetProperty("Answer").GetInt32());
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Tight_query_layout_uses_an_overflow_menu_without_document_scrollbars()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");
        await page.SetViewportSizeAsync(560, 600);

        var toolbar = page.GetByTestId("query-toolbar");
        var more = toolbar.GetByRole(AriaRole.Button, new() { Name = "More query actions" });
        await Assertions.Expect(more).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.documentElement.scrollWidth <= document.documentElement.clientWidth
                && document.documentElement.scrollHeight <= document.documentElement.clientHeight
            """));

        await more.ClickAsync();
        await Assertions.Expect(toolbar.Locator(".saved-select")).ToBeVisibleAsync();
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Surfaces_query_failures_and_restores_the_toolbar()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "boom");

        await page.GetByTestId("query-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("query-results").GetByText("kaboom", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("Failed");
        await Assertions.Expect(page.GetByTestId("query-run")).ToBeEnabledAsync();
        await Assertions.Expect(page.GetByTestId("query-cancel")).ToBeDisabledAsync();
        browserPage.AssertNoUnexpectedErrors("400");
    }

    [Fact]
    public async Task Publishes_a_query_from_its_result_toolbar()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("publish-api")).ToBeVisibleAsync();

        await page.GetByTestId("publish-api").ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Publish as API endpoint" });
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByTestId("publish-name").FillAsync("Browser answers");
        await Assertions.Expect(dialog.GetByTestId("publish-route")).ToHaveValueAsync("browser-answers");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Published: GET /gridlet/pub/browser-answers", new() { Exact = true })).ToBeVisibleAsync();

        using var client = new HttpClient { BaseAddress = fixture.BaseAddress };
        using var response = await client.GetAsync("/gridlet/pub/browser-answers");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(42, payload.RootElement.GetProperty("rows")[0].GetProperty("Answer").GetInt32());
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Sends_table_designer_and_row_editor_changes_to_the_provider()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("Create table").ClickAsync();
        await page.GetByTestId("table-name").FillAsync("AuditLog");
        await page.GetByTestId("create-table").ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Table dbo.AuditLog created.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("createTable dbo.AuditLog (1 columns)", fixture.Provider.Calls);

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();
        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Row" }).ClickAsync();
        var name = panel.GetByLabel("Name", new() { Exact = true });
        await name.FillAsync("Katherine");
        await name.PressAsync("Control+Enter");
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Row inserted.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("insert dbo.Customers (Name)", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Table_definition_is_one_editable_highlighted_SQL_editor()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Definition", Exact = true }).ClickAsync();

        var editor = panel.GetByTestId("table-definition-editor");
        var control = panel.Locator(".sql-editor:has([data-testid='table-definition-editor'])");
        await Assertions.Expect(editor).ToBeEditableAsync();
        await Assertions.Expect(panel.Locator(".sql-editor")).ToHaveCountAsync(1);
        await Assertions.Expect(panel.Locator("details, .definition-section h3")).ToHaveCountAsync(0);
        await Assertions.Expect(control.Locator(".sql-highlight .sql-keyword").First)
            .ToHaveTextAsync("CREATE");
        await editor.FillAsync("CREATE TABLE [dbo].[Replacement] ([Id] int NOT NULL);");
        await Assertions.Expect(control.Locator(".sql-highlight .sql-keyword").First)
            .ToHaveTextAsync("CREATE");
        Assert.Equal("sql", await control.GetAttributeAsync("data-editor-language"));
        browserPage.AssertNoUnexpectedErrors();
    }

    private static ILocator ActivePanel(IPage page) => page.Locator("#panels .panel:not([hidden])");

    private static async Task OpenQueryAsync(IPage page, string sql)
    {
        await page.GotoAsync("/gridlet/");
        await page.Locator("#new-query-btn").ClickAsync();
        var editor = page.GetByTestId("sql-editor");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        await editor.FillAsync(sql);
    }

    private static async Task<string> ReadDownloadAsync(IDownload download)
    {
        await using var stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
