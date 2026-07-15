using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Gridlet.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class GridletUiTests(BrowserAppFixture fixture)
{
    [Fact]
    public async Task Talks_with_the_database_using_an_ephemeral_user_key()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var credentialsBefore = fixture.Agent.StoredCredentials.Count;
        var requestsBefore = fixture.Agent.Requests.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-scope"))
            .ToContainTextAsync("Main / FakeDb");
        await Assertions.Expect(page.GetByTestId("agent-disclosure"))
            .ToContainTextAsync("external provider");

        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Summarize the customers");
        await page.GetByTestId("agent-send").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("Fake data response");
        await Assertions.Expect(page.GetByTestId("agent-status")).ToHaveTextAsync("Complete");
        Assert.Equal(credentialsBefore + 1, fixture.Agent.StoredCredentials.Count);
        Assert.Equal(requestsBefore + 1, fixture.Agent.Requests.Count);
        Assert.Equal("Summarize the customers", fixture.Agent.Requests[^1].Message);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Opens_each_ask_click_as_a_new_conversation_tab()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-open").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-panel")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#tabbar .tab").Filter(new() { HasText = "Ask — FakeDb" }))
            .ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#panels .agent-panel:not([hidden])"))
            .ToHaveCountAsync(1);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Preserves_conversation_context_when_switching_models()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var requestsBefore = fixture.Agent.Requests.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("First question");
        await page.GetByTestId("agent-send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-status")).ToHaveTextAsync("Complete");

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await Assertions.Expect(page.GetByTestId("agent-message-user"))
            .ToContainTextAsync("First question");
        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("Fake data response");

        await page.GetByTestId("agent-composer").FillAsync("Follow-up question");
        await page.GetByTestId("agent-send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-status")).ToHaveTextAsync("Complete");

        Assert.Equal(requestsBefore + 2, fixture.Agent.Requests.Count);
        var followUp = fixture.Agent.Requests[^1];
        Assert.Equal("fake-local", followUp.ProfileId);
        Assert.Equal(2, followUp.History.Count);
        Assert.Equal("First question", followUp.History[0].Content);
        Assert.Equal("Fake data response", followUp.History[1].Content);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Renders_agent_reasoning_and_markdown_tables()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Show markdown join logic");
        await page.GetByTestId("agent-send").ClickAsync();

        var assistant = page.GetByTestId("agent-message-assistant");
        await Assertions.Expect(assistant.Locator(".agent-reasoning")).ToContainTextAsync("Thought for");
        await Assertions.Expect(assistant.Locator(".agent-reasoning")).Not.ToHaveAttributeAsync("open", "");
        await assistant.Locator(".agent-reasoning summary").ClickAsync();
        await Assertions.Expect(assistant.Locator(".agent-reasoning-body"))
            .ToContainTextAsync("compact tabular answer");
        await Assertions.Expect(assistant.Locator(".agent-reasoning-summary"))
            .ToHaveCountAsync(2);
        await Assertions.Expect(assistant.Locator(".agent-reasoning-raw"))
            .ToContainTextAsync("Optional model-supplied raw reasoning");
        await Assertions.Expect(assistant.Locator(".agent-reasoning-final"))
            .ToContainTextAsync("Authoritative completed reasoning summary");
        await Assertions.Expect(assistant.Locator(".agent-reasoning-raw-final"))
            .ToContainTextAsync("Authoritative completed raw reasoning");
        await Assertions.Expect(assistant.Locator(".agent-tool-call"))
            .ToContainTextAsync("Calling describe_table");
        await Assertions.Expect(assistant.Locator(".agent-tool-result"))
            .ToContainTextAsync("Result from describe_table");
        await Assertions.Expect(assistant.Locator("strong"))
            .ToHaveTextAsync("Explanation of the join logic:");
        await Assertions.Expect(assistant.Locator(".agent-table")).ToBeVisibleAsync();
        await Assertions.Expect(assistant.Locator(".agent-table th").Nth(0)).ToHaveTextAsync("Step");
        await Assertions.Expect(assistant.Locator(".agent-table code").Nth(0)).ToHaveTextAsync("Orders");
        await Assertions.Expect(page.GetByTestId("agent-status")).ToHaveTextAsync("Complete");
        browserPage.AssertNoUnexpectedErrors();
    }

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
    public async Task Header_pickers_use_themed_dropdowns_and_keep_native_values_in_sync()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var trigger = page.GetByRole(AriaRole.Button, new() { Name = "Connection: Main" });
        await trigger.ClickAsync();
        var menu = page.GetByRole(AriaRole.Listbox, new() { Name = "Connection" });
        await Assertions.Expect(menu).ToBeVisibleAsync();
        Assert.True(await menu.EvaluateAsync<bool>("""
            element => {
                const style = getComputedStyle(element);
                return style.borderRadius === '10px'
                    && style.backgroundColor === getComputedStyle(
                        document.querySelector('.select-trigger')).backgroundColor;
            }
            """));

        await menu.GetByRole(AriaRole.Option, new() { Name = "SQLite" }).ClickAsync();
        await Assertions.Expect(page.Locator("#connection-select")).ToHaveValueAsync("SQLite");
        await Assertions.Expect(page.GetByRole(
            AriaRole.Button, new() { Name = "Connection: SQLite" })).ToBeVisibleAsync();
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
    public async Task Tailors_object_explorer_and_designer_to_provider_capabilities()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("#connection-select").SelectOptionAsync("SQLite");
        await Assertions.Expect(page.Locator("#database-select")).ToHaveValueAsync("FakeDb");

        var summaries = page.Locator("#tree summary");
        await Assertions.Expect(summaries.Filter(new() { HasText = "Tables" })).ToHaveCountAsync(1);
        await Assertions.Expect(summaries.Filter(new() { HasText = "Views" })).ToHaveCountAsync(1);
        await Assertions.Expect(summaries.Filter(new() { HasText = "Schemas" })).ToHaveCountAsync(0);
        await Assertions.Expect(summaries.Filter(new() { HasText = "Stored procedures" })).ToHaveCountAsync(0);
        await Assertions.Expect(summaries.Filter(new() { HasText = "Functions" })).ToHaveCountAsync(0);
        await Assertions.Expect(summaries.Filter(new() { HasText = "Triggers" })).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTitle("dbo.Customers").GetByText("Customers", new() { Exact = true }))
            .ToBeVisibleAsync();

        await page.GetByTitle("Create table").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByTestId("table-schema")).ToHaveValueAsync("main");
        await Assertions.Expect(panel.Locator(".designer-grid input").Nth(1)).ToHaveValueAsync("INTEGER");
        Assert.Equal(
            ["INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC"],
            await page.Locator("#gridlet-types option").EvaluateAllAsync<string[]>(
                "options => options.map(option => option.value)"));
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

    [Fact]
    public async Task Creates_views_procedures_functions_and_triggers_from_the_sidebar()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        var scenarios = new[]
        {
            (Button: "Create view", Sql: "CREATE VIEW dbo.NewView\nAS\n    SELECT 1 AS Value;"),
            (Button: "Create stored procedure", Sql: "CREATE PROCEDURE dbo.NewProcedure\nAS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT 1 AS Value;\nEND;"),
            (Button: "Create function", Sql: "CREATE FUNCTION dbo.NewFunction (@value int)\nRETURNS int\nAS\nBEGIN\n    RETURN @value;\nEND;"),
            (Button: "Create trigger", Sql: "CREATE TRIGGER dbo.NewTrigger\nON dbo.Customers\nAFTER INSERT\nAS\nBEGIN\n    SELECT 1;\nEND;"),
        };

        foreach (var scenario in scenarios)
        {
            await page.GetByTitle(scenario.Button).ClickAsync();
            var panel = ActivePanel(page);
            await Assertions.Expect(panel.GetByTestId("sql-editor")).ToHaveValueAsync(scenario.Sql);
            await panel.GetByTestId("query-run").ClickAsync();
            await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
            Assert.Equal(scenario.Sql, fixture.Provider.LastQuerySql);
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Uses_provider_specific_trigger_editing_for_sql_server_and_sqlite()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("summary").Filter(new() { HasText = "Triggers" }).ClickAsync();
        await page.GetByTitle("dbo.AuditCustomers").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByTestId("sql-editor")).ToHaveValueAsync(
            "ALTER TRIGGER dbo.AuditCustomers ON dbo.Customers AFTER INSERT AS SELECT 1;");

        await page.Locator("#connection-select").SelectOptionAsync("SQLite");
        await page.Locator("summary").Filter(new() { HasText = "Triggers" }).ClickAsync();
        await page.GetByTitle("dbo.AuditCustomers").ClickAsync();
        panel = ActivePanel(page);
        const string definition =
            "CREATE TRIGGER AuditCustomers AFTER INSERT ON Customers BEGIN SELECT 2; END;";
        await panel.GetByTestId("sql-editor").FillAsync(definition);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();

        Assert.Equal(
            "BEGIN IMMEDIATE;\nDROP TRIGGER IF EXISTS [dbo].[AuditCustomers];\n" + definition + "\nCOMMIT;",
            fixture.Provider.LastQuerySql);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Edits_an_existing_schema_object_definition()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("summary").Filter(new() { HasText = "Views" }).ClickAsync();
        await page.GetByTitle("dbo.vw_Orders").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Definition", Exact = true }).ClickAsync();

        const string sql = "ALTER VIEW dbo.vw_Orders AS SELECT 2 AS Two;";
        await panel.GetByTestId("sql-editor").FillAsync(sql);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "dbo.vw_Orders updated.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(sql, fixture.Provider.LastQuerySql);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Adds_primary_and_foreign_keys_from_the_structure_designer()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.NoKeys").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Primary key", Exact = true }).ClickAsync();
        var primaryKeyDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add primary key" });
        await primaryKeyDialog.GetByLabel("Id", new() { Exact = true }).CheckAsync();
        await primaryKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Add primary key", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Primary key added.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("addPrimaryKey dbo.NoKeys.PK_NoKeys", fixture.Provider.Calls);

        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Foreign key", Exact = true }).ClickAsync();
        var foreignKeyDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add foreign key" });
        await foreignKeyDialog.Locator("select").First.SelectOptionAsync("dbo\0Customers");
        await Assertions.Expect(foreignKeyDialog.Locator(".constraint-pair")).ToHaveCountAsync(1);
        await foreignKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Add foreign key", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Foreign key added.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("addForeignKey dbo.NoKeys.FK_NoKeys_Customers", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Displays_indexes_and_executes_index_ddl()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Cell, new() { Name = "IX_Customers_Name" }))
            .ToBeVisibleAsync();

        const string sql = "CREATE UNIQUE INDEX IX_Customers_Name_Unique ON dbo.Customers ([Name]);";
        await page.Locator("#new-query-btn").ClickAsync();
        panel = ActivePanel(page);
        await panel.GetByTestId("sql-editor").FillAsync(sql);
        await panel.GetByTestId("query-run").ClickAsync();

        await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        Assert.Equal(sql, fixture.Provider.LastQuerySql);
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
