using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var composerShell = page.GetByTestId("agent-composer-shell");
        await Assertions.Expect(composerShell).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(composerShell.GetByTestId("agent-cancel")).ToHaveCountAsync(0);
        await Assertions.Expect(composerShell.GetByTestId("agent-send")).ToHaveCountAsync(1);
        await Assertions.Expect(composerShell.GetByTestId("agent-send"))
            .ToHaveAttributeAsync("aria-label", "Send message");
        await Assertions.Expect(page.GetByTestId("agent-status")).ToHaveClassAsync(new Regex("sr-only"));
        var composer = page.GetByTestId("agent-composer");
        Assert.Equal("none", await composer.EvaluateAsync<string>(
            "element => getComputedStyle(element).resize"));
        var compactHeight = await composer.EvaluateAsync<int>("element => element.offsetHeight");
        await composer.FillAsync(string.Join('\n', Enumerable.Repeat("A longer prompt line", 30)));
        var expandedHeight = await composer.EvaluateAsync<int>("element => element.offsetHeight");
        Assert.True(expandedHeight > compactHeight);
        Assert.InRange(expandedHeight, 1, 180);
        Assert.Equal("auto", await composer.EvaluateAsync<string>(
            "element => getComputedStyle(element).overflowY"));
        await Assertions.Expect(page.Locator(".agent-header")).ToHaveCountAsync(0);
        // The welcome card makes both the AI warning and the initial sharing choice prominent.
        await Assertions.Expect(page.GetByTestId("agent-welcome-disclaimer"))
            .ToContainTextAsync("AI-generated queries may be incorrect");
        var welcomeAccess = page.GetByTestId("agent-welcome-access");
        var welcomeShareTrigger = welcomeAccess.GetByTestId("agent-welcome-share-trigger");
        await Assertions.Expect(welcomeShareTrigger).ToContainTextAsync("Schema");
        await welcomeShareTrigger.ClickAsync();
        var welcomeShareMenu = welcomeAccess.Locator(".agent-share-menu");
        await Assertions.Expect(welcomeShareMenu).ToBeVisibleAsync();
        await welcomeShareMenu.Locator("[data-scope='schema']").ClickAsync();
        await Assertions.Expect(welcomeShareTrigger).ToContainTextAsync("None (no access)");
        await Assertions.Expect(page.GetByTestId("agent-welcome-access")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("agent-share-marker")).ToHaveCountAsync(0);
        await welcomeShareMenu.Locator("[data-scope='schema']").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        // Sharing is opt-in per scope and lives in one menu: schema starts shared, the two scopes
        // that can disclose row values do not.
        var shareTrigger = composerShell.GetByTestId("agent-share-trigger");
        await Assertions.Expect(shareTrigger).ToContainTextAsync("Sharing schema");
        await shareTrigger.ClickAsync();
        var shareMenu = composerShell.Locator(".agent-share-menu");
        await Assertions.Expect(shareMenu).ToBeVisibleAsync();
        await Assertions.Expect(shareMenu.Locator(".agent-share-menu-header"))
            .ToContainTextAsync("Data shared with AI Agent");
        Assert.False(await shareMenu.EvaluateAsync<bool>(
            "element => element.scrollHeight > element.clientHeight"));
        var shareTriggerBounds = await shareTrigger.BoundingBoxAsync();
        var shareMenuBounds = await shareMenu.BoundingBoxAsync();
        Assert.NotNull(shareTriggerBounds);
        Assert.NotNull(shareMenuBounds);
        Assert.InRange(Math.Abs(shareMenuBounds.X - shareTriggerBounds.X), 0, 1);
        var shareHelp = composerShell.GetByTestId("agent-share-help");
        await Assertions.Expect(shareHelp).ToContainTextAsync("Main / FakeDb");
        await Assertions.Expect(shareHelp).ToContainTextAsync("external model provider");
        await Assertions.Expect(shareHelp).ToContainTextAsync("change sharing at any time");
        await Assertions.Expect(shareHelp).ToBeHiddenAsync();
        await shareMenu.GetByTestId("agent-share-info").FocusAsync();
        await Assertions.Expect(shareHelp).ToBeVisibleAsync();
        await Assertions.Expect(composerShell.GetByTestId("agent-privacy-tooltip")).ToHaveCountAsync(0);
        Assert.Null(await shareTrigger.GetAttributeAsync("title"));
        var shareSchema = composerShell.GetByTestId("agent-share-schema");
        var shareData = composerShell.GetByTestId("agent-share-data");
        var shareApi = composerShell.GetByTestId("agent-share-api");
        await Assertions.Expect(shareSchema).ToBeCheckedAsync();
        await Assertions.Expect(shareData).Not.ToBeCheckedAsync();
        await Assertions.Expect(shareApi).Not.ToBeCheckedAsync();
        await shareMenu.Locator("[data-scope='schema']").ClickAsync();
        await Assertions.Expect(shareSchema).Not.ToBeCheckedAsync();
        await Assertions.Expect(shareTrigger).ToContainTextAsync("Not sharing");
        await Assertions.Expect(composerShell.Page.GetByTestId("agent-share-marker"))
            .ToHaveTextAsync("You stopped sharing the database schema with the agent.");
        await Assertions.Expect(composerShell.GetByTestId("agent-share"))
            .ToHaveAttributeAsync("data-sharing", "none");
        await Assertions.Expect(shareHelp)
            .ToContainTextAsync("no database or published API access");
        Assert.Equal("none", await shareTrigger.EvaluateAsync<string>(
            "element => getComputedStyle(element, '::after').display"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const icon = document.querySelector('[data-testid="agent-share"] .agent-share-icon');
                const check = icon.querySelector('.agent-share-check');
                const warning = icon.querySelector('.agent-share-warning');
                const swatch = document.createElement('span');
                swatch.style.color = 'var(--ok)';
                document.body.append(swatch);
                const expected = getComputedStyle(swatch).color;
                const actual = getComputedStyle(icon).color;
                swatch.remove();
                return actual === expected
                    && getComputedStyle(check).display !== 'none'
                    && getComputedStyle(warning).display === 'none';
            }
            """));
        // API access is independent: it exposes no direct query access and shares a response only
        // when an endpoint is actually requested.
        await Assertions.Expect(shareMenu.Locator("[data-scope='api']"))
            .ToContainTextAsync("does not grant direct database data access");
        await Assertions.Expect(shareMenu.Locator("[data-scope='api']"))
            .ToContainTextAsync("that response is shared");
        await Assertions.Expect(shareMenu.Locator("[data-scope='api']"))
            .Not.ToContainTextAsync("GET");
        await shareMenu.Locator("[data-scope='api']").ClickAsync();
        await Assertions.Expect(shareApi).ToBeCheckedAsync();
        await Assertions.Expect(shareData).Not.ToBeCheckedAsync();
        await Assertions.Expect(shareHelp).ToContainTextAsync("published API definitions");
        await Assertions.Expect(shareHelp).ToContainTextAsync("separate from Data access");
        await Assertions.Expect(shareHelp)
            .ToContainTextAsync("only when the agent requests it");
        await shareMenu.Locator("[data-scope='api']").ClickAsync();
        await shareMenu.Locator("[data-scope='schema']").ClickAsync();
        await Assertions.Expect(composerShell.GetByTestId("agent-provider")).ToHaveCountAsync(1);
        await shareMenu.Locator("[data-scope='data']").ClickAsync();
        await Assertions.Expect(shareData).ToBeCheckedAsync();
        await Assertions.Expect(shareTrigger).ToContainTextAsync("Sharing schema + data");
        await Assertions.Expect(composerShell.Page.GetByTestId("agent-share-marker")).ToHaveCountAsync(5);
        // Several scopes can be chosen at once, so a toggle leaves the menu open.
        await Assertions.Expect(shareMenu).ToBeVisibleAsync();
        await composerShell.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(shareMenu).ToBeHiddenAsync();
        var providerControl = composerShell.Locator(".agent-provider-control");
        var providerTrigger = providerControl.Locator(".select-trigger");
        var providerMenu = providerControl.Locator(".select-menu");
        await Assertions.Expect(providerTrigger).ToBeVisibleAsync();
        await providerTrigger.ClickAsync();
        await Assertions.Expect(providerMenu).ToBeVisibleAsync();
        Assert.Equal(
            await composerShell.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"),
            await providerMenu.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        var triggerBounds = await providerTrigger.BoundingBoxAsync();
        var menuBounds = await providerMenu.BoundingBoxAsync();
        Assert.NotNull(triggerBounds);
        Assert.NotNull(menuBounds);
        Assert.True(menuBounds.Y + menuBounds.Height <= triggerBounds.Y + 1);
        await providerTrigger.ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await composer.FillAsync("Summarize the customers");
        await page.GetByTestId("agent-send").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-message-user"))
            .ToContainTextAsync("Summarize the customers");
        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("Fake data response");
        await ExpectAgentComplete(page);
        Assert.Equal(credentialsBefore + 1, fixture.Agent.StoredCredentials.Count);
        Assert.Equal(requestsBefore + 1, fixture.Agent.Requests.Count);
        Assert.Equal("Summarize the customers", fixture.Agent.Requests[^1].Message);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Dictates_into_the_composer_with_browser_speech_recognition()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.AddInitScriptAsync("""
            class FakeSpeechRecognition {
                constructor() { window.__fakeSpeechRecognition = this; }
                start() { this.onstart?.(); }
                stop() { this.stopped = true; this.onend?.(); }
                abort() { this.aborted = true; this.onerror?.({ error: 'aborted' }); this.onend?.(); }
            }
            window.SpeechRecognition = FakeSpeechRecognition;
            """);
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var composer = page.GetByTestId("agent-composer");
        var dictation = page.GetByTestId("agent-dictation");
        await Assertions.Expect(dictation).ToBeVisibleAsync();
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-pressed", "false");
        await composer.FillAsync("Please");

        await dictation.ClickAsync();
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-label", "Stop dictation");
        Assert.True(await page.EvaluateAsync<bool>(
            "window.__fakeSpeechRecognition.continuous && window.__fakeSpeechRecognition.interimResults"));
        // Speech services reject bare subtags such as the document's 'en', so the
        // requested language must carry a region.
        Assert.Matches("^[A-Za-z]{2,3}-[A-Za-z0-9]{2,}",
            await page.EvaluateAsync<string>("window.__fakeSpeechRecognition.lang"));

        await page.EvaluateAsync("""
            () => {
                const result = [{ transcript: 'list the recent orders' }];
                result.isFinal = false;
                window.__fakeSpeechRecognition.onresult({ results: [result] });
            }
            """);
        await Assertions.Expect(composer).ToHaveValueAsync("Please list the recent orders");

        await dictation.ClickAsync();
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-label", "Start dictation");

        await dictation.ClickAsync();
        await composer.FillAsync("Typed instead");
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.True(await page.EvaluateAsync<bool>("window.__fakeSpeechRecognition.aborted"));

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await composer.FillAsync(string.Empty);
        await page.EvaluateAsync("window.__fakeSpeechRecognition.aborted = false");
        await dictation.ClickAsync();
        await page.EvaluateAsync("""
            () => {
                const result = [{ transcript: 'send this dictated prompt' }];
                result.isFinal = false;
                window.__fakeSpeechRecognition.onresult({ results: [result] });
            }
            """);
        await page.GetByTestId("agent-send").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("window.__fakeSpeechRecognition.aborted"));
        await Assertions.Expect(dictation).ToHaveAttributeAsync("aria-pressed", "false");

        // A browser may still emit a queued speech result while aborting. It belongs to the
        // submitted turn and must not restore the prompt in the now-empty composer.
        await page.EvaluateAsync("""
            () => {
                const result = [{ transcript: 'stale final result' }];
                result.isFinal = true;
                window.__fakeSpeechRecognition.onresult({ results: [result] });
            }
            """);
        await Assertions.Expect(composer).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(page.GetByTestId("agent-message-user"))
            .ToContainTextAsync("send this dictated prompt");
        await ExpectAgentComplete(page);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Explains_dictation_when_the_browser_has_no_speech_recognition()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.AddInitScriptAsync("""
            delete window.SpeechRecognition;
            delete window.webkitSpeechRecognition;
            """);
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var dictation = page.GetByTestId("agent-dictation");
        await Assertions.Expect(dictation).ToBeVisibleAsync();
        await Assertions.Expect(dictation).ToHaveAttributeAsync("data-state", "unsupported");
        await Assertions.Expect(dictation).ToBeDisabledAsync();
        await Assertions.Expect(dictation).ToHaveAttributeAsync(
            "title", new Regex("not supported in this browser"));
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Narrow_composer_collapses_options_without_wrapping_or_squashing_icon_buttons()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.AddInitScriptAsync("""
            window.SpeechRecognition = class { start() {} stop() {} abort() {} };
            """);
        await page.SetViewportSizeAsync(800, 600);
        await page.GotoAsync("/gridlet/");
        await page.GetByTestId("agent-open").ClickAsync();

        var composer = page.GetByTestId("agent-composer-shell");
        var options = composer.Locator(".agent-composer-options");
        var optionsButton = composer.GetByRole(
            AriaRole.Button, new() { Name = "Chat options" });
        await Assertions.Expect(optionsButton).ToBeVisibleAsync();
        await Assertions.Expect(options).ToBeHiddenAsync();
        var shareTrigger = composer.GetByTestId("agent-share-trigger");
        await Assertions.Expect(shareTrigger).ToBeVisibleAsync();
        var actionsBounds = await composer.Locator(".agent-compose-actions").BoundingBoxAsync();
        var shareBounds = await shareTrigger.BoundingBoxAsync();
        Assert.NotNull(actionsBounds);
        Assert.NotNull(shareBounds);
        Assert.InRange(shareBounds.X - actionsBounds.X, 0, 20);
        var compactMetrics = await composer.EvaluateAsync<float[]>("""
            element => {
                const actionElement = element.querySelector('.agent-compose-actions');
                const actions = actionElement.getBoundingClientRect();
                const buttonWidths = ['.agent-dictation-button',
                    '.agent-composer-overflow > summary', '.agent-composer-submit']
                    .map(selector => element.querySelector(selector).getBoundingClientRect().width);
                return [actions.height, actions.width, actionElement.scrollWidth, ...buttonWidths];
            }
            """);
        Assert.True(compactMetrics[0] < 50, $"Action row height was {compactMetrics[0]}px.");
        Assert.True(compactMetrics[1] >= compactMetrics[2],
            $"Action row width was {compactMetrics[1]}px with {compactMetrics[2]}px of content.");
        Assert.All(compactMetrics.Skip(3), width => Assert.True(width >= 34,
            $"Icon button width was {width}px."));

        await optionsButton.ClickAsync();
        await Assertions.Expect(options).ToBeVisibleAsync();
        await Assertions.Expect(options.Locator(".agent-option-label")).ToHaveCountAsync(2);
        var providerTrigger = options.Locator(".agent-provider-control .select-trigger");
        await providerTrigger.ClickAsync();
        await Assertions.Expect(options.GetByRole(
            AriaRole.Listbox, new() { Name = "Agent model" })).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.documentElement.scrollWidth <= document.documentElement.clientWidth
            """));
        await page.GetByTestId("agent-messages").ClickAsync();
        await Assertions.Expect(options).ToBeHiddenAsync();
        await Assertions.Expect(composer.Locator(".agent-composer-overflow"))
            .Not.ToHaveAttributeAsync("open", "");

        await page.SetViewportSizeAsync(1100, 600);
        await Assertions.Expect(optionsButton).ToBeHiddenAsync();
        await Assertions.Expect(options).ToBeVisibleAsync();
        var inlineProvider = options.Locator(".agent-provider-control .select-trigger");
        var inlineEffort = options.Locator(".agent-effort-control .select-trigger");
        await Assertions.Expect(inlineProvider.Locator(".select-value-full")).ToBeHiddenAsync();
        await Assertions.Expect(inlineProvider.Locator(".select-value-compact"))
            .ToHaveTextAsync("fake-model-v1");
        await Assertions.Expect(inlineProvider.Locator(".select-value-compact")).ToBeVisibleAsync();
        await Assertions.Expect(inlineEffort).ToContainTextAsync("Medium");
        Assert.Equal("none", await inlineProvider.EvaluateAsync<string>(
            "element => getComputedStyle(element, '::after').display"));
        Assert.Equal("none", await inlineEffort.EvaluateAsync<string>(
            "element => getComputedStyle(element, '::after').display"));

        await page.SetViewportSizeAsync(1400, 600);
        await Assertions.Expect(optionsButton).ToBeHiddenAsync();
        await Assertions.Expect(options).ToBeVisibleAsync();
        await Assertions.Expect(inlineProvider.Locator(".select-value-full")).ToBeVisibleAsync();
        await Assertions.Expect(inlineProvider.Locator(".select-value-compact")).ToBeHiddenAsync();
        Assert.True(await options.Locator(".agent-option-label").EvaluateAllAsync<bool>(
            "labels => labels.every(label => getComputedStyle(label).display === 'none')"));
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
        await Assertions.Expect(page.Locator("#tabbar .tab").Filter(new() { HasText = "Ask - FakeDb" }))
            .ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#panels .agent-panel:not([hidden])"))
            .ToHaveCountAsync(1);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task New_ask_tabs_inherit_the_last_used_model_and_reasoning_effort()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var activePanel = page.Locator("#panels .agent-panel:not([hidden])");
        await activePanel.GetByTestId("agent-provider").SelectOptionAsync("fake-local");

        await page.GetByTestId("agent-open").ClickAsync();
        activePanel = page.Locator("#panels .agent-panel:not([hidden])");
        await Assertions.Expect(activePanel.GetByTestId("agent-provider"))
            .ToHaveValueAsync("fake-local");

        await activePanel.GetByTestId("agent-provider").SelectOptionAsync("fake-remote");
        await activePanel.GetByTestId("agent-effort").SelectOptionAsync("high");

        await page.GetByTestId("agent-open").ClickAsync();
        activePanel = page.Locator("#panels .agent-panel:not([hidden])");
        await Assertions.Expect(activePanel.GetByTestId("agent-provider"))
            .ToHaveValueAsync("fake-remote");
        await Assertions.Expect(activePanel.GetByTestId("agent-effort"))
            .ToHaveValueAsync("high");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Reuses_one_provider_conversation_per_ask_tab_and_closes_it_with_the_tab()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var requestsBefore = fixture.Agent.Requests.Count;
        var closesBefore = fixture.Agent.ClosedConversations.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("First question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        await page.GetByTestId("agent-composer").FillAsync("Second question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        Assert.Equal(requestsBefore + 2, fixture.Agent.Requests.Count);
        var firstConversationId = fixture.Agent.Requests[^2].ConversationId;
        Assert.False(string.IsNullOrWhiteSpace(firstConversationId));
        Assert.Equal(firstConversationId, fixture.Agent.Requests[^1].ConversationId);

        await page.Locator("#tabbar .tab.active .tab-close").ClickAsync();
        for (var attempt = 0;
             attempt < 50 && fixture.Agent.ClosedConversations.Count == closesBefore;
             attempt++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(closesBefore + 1, fixture.Agent.ClosedConversations.Count);
        Assert.Equal(firstConversationId, fixture.Agent.ClosedConversations[^1].ConversationId);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Shows_context_consumption_around_the_send_button_only_when_reported()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var gauge = page.GetByTestId("agent-context-gauge");
        var tooltip = page.GetByTestId("agent-context-tooltip");
        await Assertions.Expect(gauge).ToHaveAttributeAsync("data-context", "unknown");
        Assert.False(await page.Locator(".agent-context-ring").IsVisibleAsync());

        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Please report context usage");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        // 48k of a 64k window is 75%, the first level Gridlet calls out.
        await Assertions.Expect(gauge).ToHaveAttributeAsync("data-context", "high");
        await Assertions.Expect(tooltip).ToContainTextAsync("Context used: 48k tokens");
        await Assertions.Expect(tooltip).ToContainTextAsync("Window: 64k tokens (75%)");
        await Assertions.Expect(tooltip).ToContainTextAsync("cached 30k");
        // The summary, the window and the token breakdown each own a line.
        Assert.Equal(3, await tooltip.EvaluateAsync<int>("element => element.textContent.split('\\n').length"));
        Assert.Equal("pre-line", await tooltip
            .EvaluateAsync<string>("element => getComputedStyle(element).whiteSpace"));
        var ring = page.Locator(".agent-context-ring-value");
        var length = await ring.EvaluateAsync<double>("element => element.getTotalLength()");
        var offset = await ring.EvaluateAsync<double>(
            "element => Number(element.getAttribute('stroke-dashoffset'))");
        Assert.InRange(1 - (offset / length), 0.74, 0.76);

        await page.GetByTestId("agent-send").HoverAsync();
        await Assertions.Expect(tooltip).ToBeVisibleAsync();

        // Usage without a window must read as a plain token count, never as a proportion.
        // Counts at or above 10k are rounded to whole thousands, so 12,500 reads as "13k".
        await page.GetByTestId("agent-composer").FillAsync("Please report unsized context usage");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        await Assertions.Expect(gauge).ToHaveAttributeAsync("data-context", "unsized");
        await Assertions.Expect(tooltip).ToHaveTextAsync(
            "Context used: 13k tokens This model's context window was not reported.");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Changes_effort_without_replacing_the_conversation()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var requestsBefore = fixture.Agent.Requests.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-effort")).ToHaveValueAsync("medium");
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("First question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        await page.GetByTestId("agent-effort").SelectOptionAsync("high");
        await page.GetByTestId("agent-composer").FillAsync("Second question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        Assert.Equal(requestsBefore + 2, fixture.Agent.Requests.Count);
        var first = fixture.Agent.Requests[^2];
        var second = fixture.Agent.Requests[^1];
        Assert.Equal("medium", first.ReasoningEffort);
        Assert.Equal("high", second.ReasoningEffort);
        Assert.Equal(first.ConversationId, second.ConversationId);
        Assert.Equal(2, second.History.Count);
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
        // Only schema is shared by default, and the agent answers differently once it sees rows.
        await ShareScopeAsync(page, "data");
        await page.GetByTestId("agent-composer").FillAsync("First question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await Assertions.Expect(page.GetByTestId("agent-model-marker"))
            .ToHaveTextAsync("Now using Fake local model - fake-local-v1");
        await Assertions.Expect(page.GetByTestId("agent-message-user"))
            .ToContainTextAsync("First question");
        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("Fake data response");

        await page.GetByTestId("agent-composer").FillAsync("Follow-up question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);
        var responseFooter = page.GetByTestId("agent-message-assistant").Last
            .Locator(".agent-message-footer");
        await Assertions.Expect(responseFooter.Locator(".agent-message-role-detail"))
            .ToHaveTextAsync("· Fake local model · fake-local-v1");

        Assert.Equal(requestsBefore + 2, fixture.Agent.Requests.Count);
        var followUp = fixture.Agent.Requests[^1];
        Assert.Equal("fake-local", followUp.ProfileId);
        Assert.NotEqual(fixture.Agent.Requests[^2].ConversationId, followUp.ConversationId);
        Assert.Equal(2, followUp.History.Count);
        Assert.Equal("First question", followUp.History[0].Content);
        Assert.Equal("Fake data response", followUp.History[1].Content);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Preserves_failed_prompt_when_switching_to_a_working_model()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var requestsBefore = fixture.Agent.Requests.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await page.GetByTestId("agent-composer").FillAsync("Fail during reasoning");
        await page.GetByTestId("agent-send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-status"))
            .ToHaveAttributeAsync("data-state", "failed");

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-remote");
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Try again");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        Assert.Equal(requestsBefore + 2, fixture.Agent.Requests.Count);
        var retry = fixture.Agent.Requests[^1];
        Assert.Equal("fake-remote", retry.ProfileId);
        var failedPrompt = Assert.Single(retry.History);
        Assert.Equal("user", failedPrompt.Role);
        Assert.Equal("Fail during reasoning", failedPrompt.Content);
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
        await assistant.Locator(".agent-reasoning > summary").ClickAsync();
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
        Assert.Equal("0px", await assistant.Locator(".agent-tool-call")
            .EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        Assert.Equal("0px", await assistant.Locator(".agent-tool-result")
            .EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await Assertions.Expect(assistant.Locator(".agent-tool-result-failed"))
            .ToHaveCountAsync(0);
        await Assertions.Expect(assistant.Locator("strong"))
            .ToHaveTextAsync("Explanation of the join logic:");
        await Assertions.Expect(assistant.Locator("h3.agent-heading"))
            .ToHaveTextAsync("Query plan");
        await Assertions.Expect(assistant.Locator("em"))
            .ToHaveTextAsync("Prepared safely.");
        await Assertions.Expect(assistant.Locator("ul.agent-list li")).ToHaveCountAsync(2);
        await Assertions.Expect(assistant.Locator("hr.agent-rule")).ToHaveCountAsync(1);
        await Assertions.Expect(assistant.Locator(".agent-math")).ToHaveTextAsync("△");
        await Assertions.Expect(assistant.Locator(".agent-table")).ToBeVisibleAsync();
        await Assertions.Expect(assistant.Locator(".agent-table th").Nth(0)).ToHaveTextAsync("Step");
        await Assertions.Expect(assistant.Locator(".agent-table code").Nth(0)).ToHaveTextAsync("Orders");
        var jsonBlock = assistant.Locator(".agent-code-block").Filter(new() { HasText = "\"request\"" });
        await Assertions.Expect(jsonBlock.Locator(".json-key").Nth(0)).ToHaveTextAsync("\"request\":");
        await Assertions.Expect(jsonBlock.Locator("code")).ToContainTextAsync("\n  \"request\"");
        await jsonBlock.GetByRole(AriaRole.Button, new() { Name = "Raw" }).ClickAsync();
        await Assertions.Expect(jsonBlock.Locator("code"))
            .ToHaveTextAsync("{\"request\":{\"method\":\"GET\"},\"rows\":2,\"ok\":true}\n");
        await jsonBlock.GetByRole(AriaRole.Button, new() { Name = "Pretty" }).ClickAsync();
        await Assertions.Expect(jsonBlock.GetByRole(AriaRole.Button, new() { Name = "Pretty" }))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await ExpectAgentComplete(page);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Renders_failed_tool_results_in_red()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Show markdown failed tool");
        await page.GetByTestId("agent-send").ClickAsync();

        await page.GetByTestId("agent-message-assistant")
            .Locator(".agent-reasoning > summary").ClickAsync();
        var failedResult = page.GetByTestId("agent-message-assistant")
            .Locator(".agent-tool-result-failed");
        await Assertions.Expect(failedResult).ToBeVisibleAsync();
        await Assertions.Expect(failedResult)
            .ToContainTextAsync("Failed result from describe_table");
        Assert.True(await failedResult.EvaluateAsync<bool>(
            "element => { " +
            "const probe = document.createElement('span'); " +
            "probe.style.color = 'var(--danger)'; document.body.append(probe); " +
            "const matches = getComputedStyle(element).borderLeftColor === getComputedStyle(probe).color; " +
            "probe.remove(); return matches; }"));
        await Assertions.Expect(failedResult).ToContainTextAsync("GridletQueryException");
        await ExpectAgentComplete(page);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Copies_agent_responses_and_fenced_code_and_keeps_model_detail_accessible()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Show markdown join logic");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        var user = page.GetByTestId("agent-message-user");
        var assistant = page.GetByTestId("agent-message-assistant");
        foreach (var message in new[] { user, assistant })
        {
            var time = message.Locator(".agent-message-time");
            await Assertions.Expect(time).ToHaveAttributeAsync("datetime", new Regex("^\\d{4}-\\d{2}-\\d{2}T"));
            await Assertions.Expect(time).ToHaveTextAsync(new Regex("\\d"));
            Assert.Equal("0", await message.Locator(".agent-message-footer")
                .EvaluateAsync<string>("element => getComputedStyle(element).opacity"));
        }

        await user.HoverAsync();
        await Assertions.Expect(user.Locator(".agent-message-footer")).ToHaveCSSAsync("opacity", "1");
        await user.GetByRole(AriaRole.Button, new() { Name = "Copy your message" }).ClickAsync();
        Assert.Equal("Show markdown join logic", await page.EvaluateAsync<string>("navigator.clipboard.readText()"));

        await assistant.HoverAsync();
        await assistant.GetByRole(AriaRole.Button, new() { Name = "Copy agent response" })
            .ClickAsync();
        var responseClipboard = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
        Assert.Contains("**Explanation of the join logic:**", responseClipboard);
        Assert.Contains("```sql", responseClipboard);

        await assistant.GetByRole(AriaRole.Button, new() { Name = "Copy sql block" })
            .ClickAsync();
        var codeClipboard = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
        Assert.Equal(
            "SELECT o.orderId\nFROM Orders AS o;",
            codeClipboard.Trim().Replace("\r\n", "\n", StringComparison.Ordinal));

        var detail = assistant.Locator(".agent-message-role-detail");
        var contrastRatios = await detail.EvaluateAsync<double[]>("""
            element => {
                const channel = value => {
                    value /= 255;
                    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
                };
                const luminance = color => {
                    const [red, green, blue] = color.match(/\d+/g).slice(0, 3).map(Number);
                    return 0.2126 * channel(red) + 0.7152 * channel(green) + 0.0722 * channel(blue);
                };
                const ratio = (foreground, background) => {
                    const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
                    return (values[0] + 0.05) / (values[1] + 0.05);
                };
                const root = document.documentElement;
                const messages = element.closest('.agent-messages');
                return ['dark', 'light'].map(theme => {
                    root.dataset.theme = theme;
                    return ratio(getComputedStyle(element).color, getComputedStyle(messages).backgroundColor);
                });
            }
            """);
        Assert.All(contrastRatios, ratio => Assert.True(ratio >= 4.5, $"Contrast was {ratio:F2}:1."));
        await Assertions.Expect(detail).ToHaveCSSAsync("font-weight", "400");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Streaming_follows_the_bottom_until_the_reader_scrolls_up()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.SetViewportSizeAsync(1000, 560);
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        var messages = page.GetByTestId("agent-messages");
        var status = page.GetByTestId("agent-status");
        var composerShell = page.GetByTestId("agent-composer-shell");
        await Assertions.Expect(messages).ToHaveAttributeAsync("aria-live", "off");
        await Assertions.Expect(messages).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(composerShell).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(status).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(status).ToHaveAttributeAsync("aria-atomic", "true");

        await page.GetByTestId("agent-composer").FillAsync("Slow streaming scroll");
        await page.GetByTestId("agent-send").ClickAsync();
        await Assertions.Expect(messages).ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(composerShell).ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(composerShell.GetByTestId("agent-cancel")).ToHaveCountAsync(1);
        await Assertions.Expect(composerShell.GetByTestId("agent-send")).ToHaveCountAsync(0);
        await Assertions.Expect(composerShell.GetByTestId("agent-cancel"))
            .ToHaveAttributeAsync("aria-label", "Cancel response");
        Assert.Equal("agent-composer-outline-spin", await composerShell.EvaluateAsync<string>(
            "element => getComputedStyle(element).animationName"));
        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("Initial streamed line 100");
        var followedPosition = await messages.EvaluateAsync<int>("element => element.scrollTop");
        Assert.True(followedPosition > 0);

        await messages.EvaluateAsync("element => { element.scrollTop = 0; }");
        await page.WaitForTimeoutAsync(50);
        await Assertions.Expect(page.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync("A later streamed chunk");
        await ExpectAgentComplete(page);

        Assert.InRange(await messages.EvaluateAsync<int>("element => element.scrollTop"), 0, 1);
        await Assertions.Expect(messages).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(composerShell).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(composerShell.GetByTestId("agent-send")).ToHaveCountAsync(1);
        await Assertions.Expect(composerShell.GetByTestId("agent-send"))
            .ToHaveAttributeAsync("aria-label", "Send message");
        await Assertions.Expect(status.Locator(".agent-status-announcement"))
            .ToHaveTextAsync("Agent response complete.");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Cancelled_and_failed_turns_finalize_reasoning_and_leave_a_transcript_annotation()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await page.GetByTestId("agent-composer").FillAsync("Slow cancellation");
        await page.GetByTestId("agent-send").ClickAsync();
        var cancelledAssistant = page.GetByTestId("agent-message-assistant");
        await Assertions.Expect(cancelledAssistant.Locator(".agent-reasoning"))
            .ToContainTextAsync("Waiting on a deliberately slow provider.");
        var thinking = cancelledAssistant.Locator(".agent-reasoning.is-thinking");
        await Assertions.Expect(thinking.Locator("summary")).ToHaveTextAsync("Thinking…");
        Assert.Equal("agent-thinking-sheen", await thinking.Locator("summary span")
            .EvaluateAsync<string>("element => getComputedStyle(element).animationName"));
        Assert.Equal("rgba(0, 0, 0, 0)", await thinking
            .EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));
        Assert.Equal("rgba(0, 0, 0, 0)", await thinking
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.Equal("linear", await thinking.Locator("summary span")
            .EvaluateAsync<string>("element => getComputedStyle(element).animationTimingFunction"));
        Assert.Equal("none", await thinking.Locator("summary")
            .EvaluateAsync<string>("element => getComputedStyle(element).textDecorationLine"));
        Assert.Equal("4px", await thinking.Locator("summary")
            .EvaluateAsync<string>("element => getComputedStyle(element, '::after').width"));
        await thinking.Locator("summary").HoverAsync();
        Assert.Equal("underline", await thinking.Locator("summary span")
            .EvaluateAsync<string>("element => getComputedStyle(element).textDecorationLine"));
        var collapsedThinkingLabel = await thinking.Locator("summary span").BoundingBoxAsync();
        await thinking.Locator("summary").ClickAsync();
        await Assertions.Expect(thinking).ToHaveAttributeAsync("open", "");
        var expandedThinkingLabel = await thinking.Locator("summary span").BoundingBoxAsync();
        Assert.NotNull(collapsedThinkingLabel);
        Assert.NotNull(expandedThinkingLabel);
        Assert.InRange(Math.Abs(expandedThinkingLabel.X - collapsedThinkingLabel.X), 0, 0.5f);
        Assert.InRange(Math.Abs(expandedThinkingLabel.Y - collapsedThinkingLabel.Y), 0, 0.5f);
        var reasoningPanelBounds = await thinking.BoundingBoxAsync();
        var messageBounds = await cancelledAssistant.BoundingBoxAsync();
        Assert.NotNull(reasoningPanelBounds);
        Assert.NotNull(messageBounds);
        Assert.True(reasoningPanelBounds.Y >= messageBounds.Y,
            "The expanded reasoning panel overflowed the agent message.");
        Assert.NotEqual("rgba(0, 0, 0, 0)", await thinking
            .EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));
        await page.GetByTestId("agent-cancel").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-status"))
            .ToHaveAttributeAsync("data-state", "cancelled");
        await Assertions.Expect(cancelledAssistant.Locator(".agent-message-error"))
            .ToHaveTextAsync("Response cancelled.");
        await Assertions.Expect(cancelledAssistant.Locator(".agent-reasoning > summary"))
            .ToContainTextAsync("Thought for");
        await Assertions.Expect(cancelledAssistant.Locator(".agent-reasoning"))
            .Not.ToHaveClassAsync(new Regex("is-thinking"));
        await Assertions.Expect(page.GetByTestId("agent-messages"))
            .ToHaveAttributeAsync("aria-busy", "false");

        await page.GetByTestId("agent-composer").FillAsync("Fail during reasoning");
        await page.GetByTestId("agent-send").ClickAsync();
        var failedAssistant = page.GetByTestId("agent-message-assistant").Last;
        await Assertions.Expect(page.GetByTestId("agent-status"))
            .ToHaveAttributeAsync("data-state", "failed");
        await Assertions.Expect(failedAssistant.Locator(".agent-message-error"))
            .ToHaveTextAsync("Deliberate streamed failure.");
        await Assertions.Expect(failedAssistant.Locator(".agent-reasoning > summary"))
            .ToContainTextAsync("Thought for");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Composer_keyboard_respects_required_credentials_and_ime_composition()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var requestsBefore = fixture.Agent.Requests.Count;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var composer = page.GetByTestId("agent-composer");
        await composer.FillAsync("Do not send without a key");
        await composer.PressAsync("Enter");
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(requestsBefore, fixture.Agent.Requests.Count);
        await Assertions.Expect(page.GetByTestId("agent-api-key")).ToBeFocusedAsync();

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await composer.EvaluateAsync("""
            element => element.dispatchEvent(new KeyboardEvent('keydown', {
                key: 'Enter', bubbles: true, cancelable: true, isComposing: true
            }))
            """);
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(requestsBefore, fixture.Agent.Requests.Count);
        await Assertions.Expect(composer).ToHaveValueAsync("Do not send without a key");

        await composer.PressAsync("Enter");
        await ExpectAgentComplete(page);
        Assert.Equal(requestsBefore + 1, fixture.Agent.Requests.Count);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Restores_chat_scroll_position_after_opening_a_query_tab()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.SetViewportSizeAsync(1000, 560);
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("Show markdown join logic");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        var messages = page.GetByTestId("agent-messages");
        await messages.EvaluateAsync("""
            element => {
                const filler = document.createElement('div');
                filler.style.height = '1200px';
                element.append(filler);
                element.scrollTop = 640;
            }
            """);
        var openQuery = page.GetByTestId("agent-open-query").First;
        await openQuery.ScrollIntoViewIfNeededAsync();
        var before = await messages.EvaluateAsync<int>("element => element.scrollTop");
        Assert.True(before > 0);

        await openQuery.DispatchEventAsync("click");
        await page.Locator("#tabbar .tab").Filter(new() { HasText = "Ask - FakeDb" }).ClickAsync();
        await Assertions.Expect(messages).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(50);

        var after = await messages.EvaluateAsync<int>("element => element.scrollTop");
        Assert.InRange(after, before - 1, before + 1);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Saves_conversations_in_the_browser_and_reopens_them_in_their_own_tab()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var chats = page.GetByTestId("agent-history");
        await Assertions.Expect(chats).ToHaveAttributeAsync("aria-label", "Saved chats");
        await Assertions.Expect(chats.GetByText("Chats", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("agent-history-toggle"))
            .ToHaveAttributeAsync("aria-label", "Hide saved chats");
        await Assertions.Expect(page.GetByTestId("agent-history-empty")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("agent-welcome-disclaimer")).ToContainTextAsync(
            "AI-generated queries may be incorrect");
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-effort").SelectOptionAsync("high");
        await page.GetByTestId("agent-composer").FillAsync("Which customers ordered most?");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        var item = page.GetByTestId("agent-history-item");
        await Assertions.Expect(item).ToHaveCountAsync(1);
        await Assertions.Expect(item.GetByTestId("agent-history-open"))
            .ToContainTextAsync("Which customers ordered most?");

        // A new chat keeps the completed transcript in history and clears the current workspace.
        await page.GetByTestId("agent-new-chat").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-history-item")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("agent-message-user")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("Ask about this database")).ToBeVisibleAsync();

        // Transcripts belong to the browser, so they survive a reload without any server storage.
        await page.ReloadAsync();
        await page.GetByTestId("agent-open").ClickAsync();
        // The effort the conversation used must win over whatever the composer is set to now.
        await page.GetByTestId("agent-effort").SelectOptionAsync("low");
        await page.GetByTestId("agent-history-open").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-panel")).ToHaveCountAsync(2);
        var restored = page.GetByTestId("agent-panel").Last;
        await Assertions.Expect(restored.GetByTestId("agent-message-user"))
            .ToContainTextAsync("Which customers ordered most?");
        // What the agent answered is beside the point here; that the answer came back is not.
        await Assertions.Expect(restored.GetByTestId("agent-message-assistant"))
            .ToContainTextAsync(new Regex("Fake (data|schema) response"));
        await Assertions.Expect(restored.GetByTestId("agent-provider")).ToHaveValueAsync("fake-remote");
        await Assertions.Expect(restored.GetByTestId("agent-effort")).ToHaveValueAsync("high");

        // A conversation that is already open is brought forward instead of opening a second copy.
        await restored.GetByTestId("agent-history-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-panel")).ToHaveCountAsync(2);

        await restored.GetByTestId("agent-history-delete").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-history-item")).ToHaveCountAsync(0);
        // Deleting the last conversation leaves nothing behind in storage.
        Assert.Null(await page.EvaluateAsync<string?>(
            "() => localStorage.getItem('gridlet.agentConversations')"));
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Remembers_the_last_model_and_the_collapsed_conversation_pane()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        var history = page.GetByTestId("agent-history");
        await Assertions.Expect(history).ToHaveAttributeAsync("data-collapsed", "false");
        var initialWidth = (await history.BoundingBoxAsync())!.Width;
        await page.GetByTestId("agent-history-grip").PressAsync("ArrowLeft");
        var resizedWidth = (await history.BoundingBoxAsync())!.Width;
        Assert.InRange(resizedWidth, initialWidth + 19, initialWidth + 21);
        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await page.GetByTestId("agent-history-toggle").ClickAsync();
        await Assertions.Expect(history).ToHaveAttributeAsync("data-collapsed", "true");
        await Assertions.Expect(page.GetByTestId("agent-history-list")).ToBeHiddenAsync();

        await page.ReloadAsync();
        await page.GetByTestId("agent-open").ClickAsync();
        // Fake remote model is the first configured profile, so this can only be the last choice.
        await Assertions.Expect(page.GetByTestId("agent-provider")).ToHaveValueAsync("fake-local");
        await Assertions.Expect(page.GetByTestId("agent-history"))
            .ToHaveAttributeAsync("data-collapsed", "true");
        await page.GetByTestId("agent-history-toggle").ClickAsync();
        var restoredWidth = (await page.GetByTestId("agent-history").BoundingBoxAsync())!.Width;
        Assert.InRange(restoredWidth, resizedWidth - 1, resizedWidth + 1);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Reopened_conversations_keep_the_model_that_answered_each_turn()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("First question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        await page.GetByTestId("agent-provider").SelectOptionAsync("fake-local");
        await page.GetByTestId("agent-composer").FillAsync("Second question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        // A conversation that changed models must not be back-dated to its last answer's model.
        await page.ReloadAsync();
        await page.GetByTestId("agent-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-history-open"))
            .ToContainTextAsync("2 models");
        await page.GetByTestId("agent-history-open").ClickAsync();

        var restored = page.GetByTestId("agent-panel").Last;
        var details = restored.Locator(".agent-message-role-detail");
        await Assertions.Expect(details).ToHaveCountAsync(2);
        await Assertions.Expect(details.First).ToHaveTextAsync("· Fake remote model · fake-model-v1");
        await Assertions.Expect(details.Last).ToHaveTextAsync("· Fake local model · fake-local-v1");
        await Assertions.Expect(restored.GetByTestId("agent-model-marker"))
            .ToHaveTextAsync("Now using Fake local model - fake-local-v1");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Reopening_a_conversation_does_not_make_it_look_newer_than_its_last_answer()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTestId("agent-open").ClickAsync();
        await page.GetByTestId("agent-api-key").FillAsync("sk-browser-only");
        await page.GetByTestId("agent-composer").FillAsync("An older question");
        await page.GetByTestId("agent-send").ClickAsync();
        await ExpectAgentComplete(page);

        // Age the saved record, then reopen and close it the way a returning reader would.
        await page.EvaluateAsync("""
            () => {
                const key = 'gridlet.agentConversations';
                const records = JSON.parse(localStorage.getItem(key));
                const twoHours = 2 * 60 * 60 * 1000;
                records[0].createdAt = Date.now() - twoHours;
                records[0].updatedAt = Date.now() - twoHours;
                localStorage.setItem(key, JSON.stringify(records));
            }
            """);
        await page.ReloadAsync();
        await page.GetByTestId("agent-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("agent-history-open")).ToContainTextAsync("2h ago");
        await page.GetByTestId("agent-history-open").ClickAsync();

        var restored = page.GetByTestId("agent-panel").Last;
        await Assertions.Expect(restored.GetByTestId("agent-message-user"))
            .ToContainTextAsync("An older question");
        await page.Locator(".tab.active .tab-close").ClickAsync();

        await Assertions.Expect(page.GetByTestId("agent-history-open")).ToContainTextAsync("2h ago");
        browserPage.AssertNoUnexpectedErrors();
    }

    private static Task ExpectAgentComplete(IPage page) =>
        Assertions.Expect(page.GetByTestId("agent-status"))
            .ToHaveAttributeAsync("data-state", "complete");

    /// <summary>
    /// Turns on one sharing scope through the composer's Share menu. Schema is the only scope on by
    /// default, so a test that expects the agent to reach anything else has to ask for it the way a
    /// person would.
    /// </summary>
    private static async Task ShareScopeAsync(IPage page, string scope)
    {
        await page.GetByTestId("agent-share-trigger").ClickAsync();
        await page.Locator($".agent-share-menu [data-scope='{scope}']").ClickAsync();
        await Assertions.Expect(page.GetByTestId($"agent-share-{scope}")).ToBeCheckedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".agent-share-menu")).ToBeHiddenAsync();
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
    public async Task Narrow_header_hides_picker_labels_then_collapses_pickers_into_app_menu()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.SetViewportSizeAsync(850, 600);
        await page.GotoAsync("/gridlet/");

        var pickers = page.GetByTestId("connection-pickers");
        await Assertions.Expect(pickers).ToBeVisibleAsync();
        Assert.True(await pickers.Locator(".picker-label").EvaluateAllAsync<bool>(
            "labels => labels.every(label => getComputedStyle(label).display === 'none')"));

        // At an intermediate width (including a 1366px window at 200% zoom), the picker
        // container must not shrink underneath its fixed-width triggers and overlap actions.
        await page.SetViewportSizeAsync(680, 600);
        await page.WaitForFunctionAsync("""
            () => {
                const topbar = document.querySelector('#topbar').getBoundingClientRect();
                const items = [...document.querySelectorAll(
                    '#topbar > .brand, #topbar > .toolbar-slot > :not([hidden]), #topbar > .toolbar-more:not([hidden])')]
                    .filter(item => item.getClientRects().length)
                    .map(item => item.getBoundingClientRect())
                    .sort((left, right) => left.left - right.left);
                return document.documentElement.scrollWidth <= document.documentElement.clientWidth
                    && items.every(item => item.left >= topbar.left && item.right <= topbar.right)
                    && items.slice(1).every((item, index) => item.left >= items[index].right - 0.5);
            }
            """);

        await page.SetViewportSizeAsync(360, 600);
        var more = page.Locator("#topbar").GetByRole(
            AriaRole.Button, new() { Name = "More app actions" });
        await Assertions.Expect(more).ToBeVisibleAsync();
        await Assertions.Expect(pickers).ToBeHiddenAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const topbar = document.querySelector('#topbar').getBoundingClientRect();
                const more = document.querySelector('#topbar .toolbar-more > summary').getBoundingClientRect();
                return topbar.height < 60
                    && more.width >= 32
                    && document.documentElement.scrollWidth <= document.documentElement.clientWidth;
            }
            """));

        await more.ClickAsync();
        await Assertions.Expect(pickers).ToBeVisibleAsync();
        Assert.True(await pickers.Locator(".picker-label").EvaluateAllAsync<bool>(
            "labels => labels.every(label => getComputedStyle(label).display !== 'none')"));
        await Assertions.Expect(page.GetByRole(
            AriaRole.Button, new() { Name = "Connection: Main" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(
            AriaRole.Button, new() { Name = "Database: FakeDb" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Connection: Main" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(
            AriaRole.Listbox, new() { Name = "Connection" })).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.documentElement.scrollWidth <= document.documentElement.clientWidth
            """));
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
        await page.GetByTitle("dbo.Customers").ClickAsync();
        await Assertions.Expect(ActivePanel(page).GetByTestId("import-data")).ToHaveCountAsync(0);

        await page.GetByTitle("Create table").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByTestId("table-schema")).ToHaveValueAsync("FakeDb");
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
    public async Task Warns_before_running_update_or_delete_without_a_where_clause()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 1\nGO\nDELETE FROM Customers");

        await page.GetByTestId("query-run").ClickAsync();

        var warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
        await Assertions.Expect(warning).ToBeVisibleAsync();
        await Assertions.Expect(warning).ToContainTextAsync(
            "DELETE statement has no top-level WHERE clause");
        await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToBeEmptyAsync();

        var editor = page.GetByTestId("sql-editor");
        await editor.FillAsync("UPDATE Customers SET Note = 'WHERE is text' /* WHERE is a comment */");
        await page.GetByTestId("query-run").ClickAsync();
        warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
        await Assertions.Expect(warning).ToBeVisibleAsync();
        await warning.GetByRole(AriaRole.Button, new() { Name = "Run anyway", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        Assert.Equal("UPDATE Customers SET Note = 'WHERE is text' /* WHERE is a comment */",
            fixture.Provider.LastQuerySql);

        await editor.FillAsync("WITH doomed AS (SELECT Id FROM Customers WHERE Active = 0) DELETE FROM doomed");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" })
            .GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await editor.FillAsync("DELETE FROM Customers WHERE Id IN (SELECT Id FROM Customers)");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToHaveCountAsync(0);

        await editor.FillAsync("SELECT 1\nDELETE FROM Customers");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" })
            .GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await editor.FillAsync(
            "DELETE FROM Customers WHERE Id = 1\nDELETE FROM Orders\nUPDATE Pizzas SET Name = 'All'");
        await page.GetByTestId("query-run").ClickAsync();
        warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
        await Assertions.Expect(warning).ToContainTextAsync(
            "2 UPDATE or DELETE statements with no top-level WHERE clause");
        await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        foreach (var prefix in new[]
                 {
                     "EXEC dbo.LogMaintenance\n",
                     "INSERT INTO AuditLog VALUES (1)\n",
                     "DROP TABLE #OldResults\n",
                 })
        {
            await editor.FillAsync(prefix + "DELETE FROM Customers");
            await page.GetByTestId("query-run").ClickAsync();
            warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
            await Assertions.Expect(warning).ToBeVisibleAsync();
            await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }

        await editor.FillAsync(
            "ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) " +
            "REFERENCES Customers (Id) ON DELETE CASCADE");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToHaveCountAsync(0);

        await editor.FillAsync("ALTER TABLE Orders ENABLE TRIGGER ALL\nDELETE FROM Customers");
        await page.GetByTestId("query-run").ClickAsync();
        warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
        await Assertions.Expect(warning).ToBeVisibleAsync();
        await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        foreach (var definition in new[]
                 {
                     "GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Orders TO app_role",
                     "CREATE TRIGGER trg AFTER DELETE ON Customers BEGIN " +
                      "DELETE FROM AuditLog; DELETE FROM AuditArchive; END;",
                     "CREATE TRIGGER trg AFTER UPDATE ON Customers BEGIN " +
                     "UPDATE AuditLog SET Value = CASE WHEN Value = 1 THEN 2 ELSE 3 END; " +
                     "DELETE FROM AuditArchive; END;",
                     "CREATE OR REPLACE FUNCTION clean_up() RETURNS void AS $$ BEGIN " +
                     "DELETE FROM AuditLog; END; $$ LANGUAGE plpgsql;",
                     "CREATE TEMP TRIGGER trg AFTER DELETE ON Customers BEGIN " +
                     "DELETE FROM AuditLog; END;",
                     "CREATE PROCEDURE clean_up AS DELETE FROM AuditLog; DELETE FROM AuditArchive;",
                     "CREATE TRIGGER trg ON Customers AFTER UPDATE AS " +
                     "DELETE FROM AuditLog; DELETE FROM AuditArchive;",
                  })
        {
            await editor.FillAsync(definition);
            await page.GetByTestId("query-run").ClickAsync();
            await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
            await Assertions.Expect(page.GetByRole(
                AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToHaveCountAsync(0);
        }

        foreach (var definitionThenMutation in new[]
                 {
                     "CREATE VIEW active_customers AS SELECT * FROM Customers; DELETE FROM Customers",
                     "ALTER VIEW active_customers AS SELECT * FROM Customers; UPDATE Customers SET Active = 0",
                     "CREATE VIEW active_customers AS SELECT * FROM Customers\nGO\nDELETE FROM Customers",
                     "CREATE PROCEDURE clean_up AS DELETE FROM AuditLog\r\nGO\r\nDELETE FROM Customers",
                  })
        {
            await editor.FillAsync(definitionThenMutation);
            await page.GetByTestId("query-run").ClickAsync();
            warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
            await Assertions.Expect(warning).ToBeVisibleAsync();
            await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }

        await editor.FillAsync("UPDATE STATISTICS Customers");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToHaveCountAsync(0);

        await editor.FillAsync("EXPLAIN QUERY PLAN DELETE FROM Customers");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await Assertions.Expect(page.GetByRole(
            AriaRole.Dialog, new() { Name = "Run query without WHERE?" })).ToHaveCountAsync(0);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Warns_after_a_completed_definition_on_non_sql_server_connections()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        await page.Locator("#connection-select").SelectOptionAsync("SQLite");
        await Assertions.Expect(page.Locator("#database-select")).ToHaveValueAsync("FakeDb");
        await page.Locator("#new-query-btn").ClickAsync();
        var editor = page.GetByTestId("sql-editor");

        foreach (var definitionThenMutation in new[]
                 {
                     "CREATE OR REPLACE FUNCTION clean_up() RETURNS void AS $$ BEGIN " +
                     "DELETE FROM AuditLog; END; $$ LANGUAGE plpgsql; DELETE FROM Customers",
                     "CREATE TRIGGER trg AFTER DELETE ON Customers FOR EACH ROW " +
                     "EXECUTE FUNCTION clean_up(); DELETE FROM Customers",
                 })
        {
            await editor.FillAsync(definitionThenMutation);
            await page.GetByTestId("query-run").ClickAsync();
            var warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
            await Assertions.Expect(warning).ToBeVisibleAsync();
            await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        }

        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Keeps_query_history_with_outcome_duration_and_sql_in_this_browser()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");
        var editor = page.GetByTestId("sql-editor");

        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await editor.FillAsync("boom");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("Failed");

        await ClickQueryActionAsync(ActivePanel(page), "query-history");
        var history = page.GetByRole(AriaRole.Dialog, new() { Name = "Query history" });
        var items = history.GetByTestId("query-history-item");
        await Assertions.Expect(items).ToHaveCountAsync(2);
        await Assertions.Expect(items.Nth(0)).ToContainTextAsync("boom");
        await Assertions.Expect(items.Nth(0)).ToContainTextAsync("Failed");
        await Assertions.Expect(items.Nth(1)).ToContainTextAsync("SELECT 42");
        await Assertions.Expect(items.Nth(1)).ToContainTextAsync("Succeeded · 1 ms");
        await history.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

        await page.EvaluateAsync("""
            () => {
                const original = Storage.prototype.setItem;
                window.__gridletOriginalSetItem = original;
                Storage.prototype.setItem = function(key, value) {
                    if (key === 'gridlet.queryHistory' && JSON.parse(value)[0]?.sql === 'oversized') {
                        throw new DOMException('Quota exceeded', 'QuotaExceededError');
                    }
                    return original.call(this, key, value);
                };
            }
            """);
        await editor.FillAsync("oversized");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        var storedAfterQuota = await page.EvaluateAsync<string[]>("""
            () => JSON.parse(localStorage.getItem('gridlet.queryHistory')).map(entry => entry.sql)
            """);
        Assert.Equal(["boom", "SELECT 42"], storedAfterQuota);

        await page.EvaluateAsync("""
            () => {
                Storage.prototype.setItem = window.__gridletOriginalSetItem;
                const original = Storage.prototype.setItem;
                let threshold;
                Storage.prototype.setItem = function(key, value) {
                    if (key === 'gridlet.queryHistory') {
                        const entries = JSON.parse(value);
                        if (entries[0]?.sql === 'fits-newest') {
                            threshold ??= JSON.stringify(entries.slice(0, 2)).length;
                            if (value.length > threshold) {
                                throw new DOMException('Quota exceeded', 'QuotaExceededError');
                            }
                        }
                    }
                    return original.call(this, key, value);
                };
            }
            """);
        await editor.FillAsync("fits-newest");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        storedAfterQuota = await page.EvaluateAsync<string[]>("""
            () => JSON.parse(localStorage.getItem('gridlet.queryHistory')).map(entry => entry.sql)
            """);
        Assert.Equal(["fits-newest", "boom"], storedAfterQuota);

        await page.ReloadAsync();
        await page.Locator("#new-query-btn").ClickAsync();
        editor = page.GetByTestId("sql-editor");
        await ClickQueryActionAsync(ActivePanel(page), "query-history");
        history = page.GetByRole(AriaRole.Dialog, new() { Name = "Query history" });
        items = history.GetByTestId("query-history-item");
        await Assertions.Expect(items).ToHaveCountAsync(2);
        await items.Nth(1).ClickAsync();
        await Assertions.Expect(editor).ToHaveValueAsync("boom");

        await ClickQueryActionAsync(ActivePanel(page), "query-history");
        history = page.GetByRole(AriaRole.Dialog, new() { Name = "Query history" });
        await history.GetByRole(AriaRole.Button, new() { Name = "Clear history", Exact = true }).ClickAsync();
        await Assertions.Expect(history.GetByTestId("query-history-empty")).ToBeVisibleAsync();
        browserPage.AssertNoUnexpectedErrors("400");
    }

    [Fact]
    public async Task Bounds_and_scopes_query_history_by_connection_and_database()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "newest");
        await page.EvaluateAsync("""
            () => {
                const records = Array.from({ length: 98 }, (_, index) => ({
                    sql: `old-${index}`,
                    startedAt: Date.now() - index - 1,
                    durationMs: 1,
                    outcome: 'succeeded',
                    connection: 'Main',
                    database: 'FakeDb',
                }));
                records.push({
                    sql: 'other-connection',
                    startedAt: Date.now() - 200,
                    durationMs: 1,
                    outcome: 'succeeded',
                    connection: 'SQLite',
                    database: 'FakeDb',
                });
                records.push({
                    sql: 'old-trimmed-1', startedAt: Date.now() - 300, durationMs: 1,
                    outcome: 'succeeded', connection: 'Main', database: 'FakeDb',
                }, {
                    sql: 'old-trimmed-2', startedAt: Date.now() - 301, durationMs: 1,
                    outcome: 'succeeded', connection: 'Main', database: 'FakeDb',
                });
                localStorage.setItem('gridlet.queryHistory', JSON.stringify(records));
            }
            """);

        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
        await ClickQueryActionAsync(ActivePanel(page), "query-history");
        var history = page.GetByRole(AriaRole.Dialog, new() { Name = "Query history" });
        var items = history.GetByTestId("query-history-item");
        await Assertions.Expect(items).ToHaveCountAsync(99);
        await Assertions.Expect(items.First).ToContainTextAsync("newest");
        await Assertions.Expect(items.GetByText("other-connection", new() { Exact = true }))
            .ToHaveCountAsync(0);
        Assert.Equal(100, await page.EvaluateAsync<int>(
            "() => JSON.parse(localStorage.getItem('gridlet.queryHistory')).length"));

        await history.GetByRole(AriaRole.Button, new() { Name = "Clear history", Exact = true }).ClickAsync();
        var remaining = await page.EvaluateAsync<string[]>("""
            () => JSON.parse(localStorage.getItem('gridlet.queryHistory')).map(entry => entry.sql)
            """);
        Assert.Equal(["other-connection"], remaining);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Formats_the_query_without_changing_literals_or_comments()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page,
            "select c.Id,c.Name from dbo.Customers c left join dbo.Orders o on o.CustomerId=c.Id where c.Note='from here' and c.Active=1 -- keep select lower");

        await page.GetByTestId("query-format").ClickAsync();

        await Assertions.Expect(page.GetByTestId("sql-editor")).ToHaveValueAsync(
            "SELECT\n" +
            "    c.Id,\n" +
            "    c.Name\n" +
            "FROM dbo.Customers c\n" +
            "LEFT JOIN dbo.Orders o\n" +
            "    ON o.CustomerId = c.Id\n" +
            "WHERE c.Note = 'from here'\n" +
            "AND c.Active = 1 -- keep select lower");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Formats_only_selected_sql_with_the_keyboard_shortcut()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "-- untouched\nselect Id,Name from Customers\n-- untouched too");
        var editor = page.GetByTestId("sql-editor");
        await editor.EvaluateAsync("""
            input => input.setSelectionRange(
                input.value.indexOf('select'),
                input.value.indexOf('\n-- untouched too'))
            """);

        await editor.PressAsync("Control+Shift+f");

        await Assertions.Expect(editor).ToHaveValueAsync(
            "-- untouched\nSELECT\n    Id,\n    Name\nFROM Customers\n-- untouched too");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Completes_partial_alias_columns_from_the_current_query_sources()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT * FROM dbo.Customers c WHERE c.Na");
        var editor = page.GetByTestId("sql-editor");

        await editor.PressAsync("Control+Space");
        var suggestion = page.Locator(".sql-completions").GetByRole(
            AriaRole.Button, new() { Name = "c.Name", Exact = true });
        await Assertions.Expect(suggestion).ToBeVisibleAsync();
        await suggestion.ClickAsync();

        await Assertions.Expect(editor).ToHaveValueAsync(
            "SELECT * FROM dbo.Customers c WHERE c.Name");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Narrows_unqualified_columns_and_offers_foreign_key_join_conditions()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT * FROM dbo.Customers c WHERE Na");
        var editor = page.GetByTestId("sql-editor");

        await editor.PressAsync("Control+Space");
        await Assertions.Expect(page.Locator(".sql-completions").GetByRole(
            AriaRole.Button, new() { Name = "Name", Exact = true })).ToBeVisibleAsync();

        await editor.FillAsync("SELECT * FROM dbo.Orders o JOIN dbo.Pizzas p ON ");
        await editor.PressAsync("Control+Space");
        var join = page.Locator(".sql-completions").GetByRole(
            AriaRole.Button, new() { Name = "o.PizzaId = p.Id", Exact = true });
        await Assertions.Expect(join).ToBeVisibleAsync();
        await join.ClickAsync();
        await Assertions.Expect(editor).ToHaveValueAsync(
            "SELECT * FROM dbo.Orders o JOIN dbo.Pizzas p ON o.PizzaId = p.Id");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Completes_routine_parameter_names()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "EXEC dbo.RefreshOrders @S");
        var editor = page.GetByTestId("sql-editor");

        await editor.PressAsync("Control+Space");
        var suggestion = page.Locator(".sql-completions").GetByRole(
            AriaRole.Button, new() { Name = "@Since", Exact = true });
        await Assertions.Expect(suggestion).ToBeVisibleAsync();
        await suggestion.ClickAsync();

        await Assertions.Expect(editor).ToHaveValueAsync("EXEC dbo.RefreshOrders @Since");
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Confirms_successful_non_row_query_execution()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "no-results");

        await page.GetByTestId("query-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("query-results").GetByText(
            "Query executed successfully — 0 records affected", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("query-status")).ToHaveTextAsync("1 ms");
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
    public async Task Enables_friendly_foreign_key_display_and_searches_when_editing()
    {
        const string settingUrl =
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Orders/foreign-key-displays/FK_Orders_Pizzas";
        using var client = new HttpClient { BaseAddress = fixture.BaseAddress };
        await client.DeleteAsync(settingUrl);

        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        await page.GetByTitle("dbo.Orders").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.Locator("tbody td:not(.row-selector)")
            .Filter(new() { HasTextRegex = new Regex("^1$") }).First).ToBeVisibleAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        var foreignKeyRow = panel.Locator("tr").Filter(new() { HasText = "FK_Orders_Pizzas" });
        await foreignKeyRow.GetByRole(AriaRole.Button, new() { Name = "Show value…", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Show foreign-key value" });
        await Assertions.Expect(dialog.GetByLabel("Foreign key label column")).ToHaveValueAsync("Name");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Show value", Exact = true }).ClickAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "Data", Exact = true }).ClickAsync();
        var margherita = panel.GetByRole(AriaRole.Cell, new() { Name = "1 Margherita", Exact = true });
        await Assertions.Expect(margherita).ToBeVisibleAsync();
        var keyColor = await margherita.Locator("span").First.EvaluateAsync<string>(
            "element => getComputedStyle(element).color");
        var labelColor = await margherita.Locator(".fk-value-label").EvaluateAsync<string>(
            "element => getComputedStyle(element).color");
        Assert.NotEqual(keyColor, labelColor);
        var broken = panel.GetByRole(AriaRole.Cell, new() { Name = "4 #REF!", Exact = true });
        await Assertions.Expect(broken).ToBeVisibleAsync();
        Assert.Equal("italic", await broken.Locator(".fk-value-label").EvaluateAsync<string>(
            "element => getComputedStyle(element).fontStyle"));
        await Assertions.Expect(panel.GetByRole(AriaRole.Cell, new() { Name = "99 Missing reference", Exact = true }))
            .ToBeVisibleAsync();

        var editCell = panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").Nth(1);
        await editCell.ClickAsync();
        await Assertions.Expect(panel.Locator("tr.row-editor")).ToHaveCountAsync(1);
        browserPage.AssertNoUnexpectedErrors();
        var pizza = panel.GetByLabel("PizzaId", new() { Exact = true });
        await Assertions.Expect(pizza).ToHaveValueAsync("1 Margherita");
        var allValues = panel.GetByRole(AriaRole.Option, new() { Name = "1 Margherita", Exact = true });
        await Assertions.Expect(allValues).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByLabel("Show choices for PizzaId")).ToHaveCountAsync(0);
        await pizza.FillAsync("");
        await Assertions.Expect(allValues).ToBeVisibleAsync();
        await Assertions.Expect(allValues).ToHaveAttributeAsync("aria-setsize", "50");
        var menu = panel.Locator(".fk-autocomplete-menu");
        Assert.False(await menu.EvaluateAsync<bool>(
            "element => element.scrollWidth > element.clientWidth"));
        Assert.True(await menu.EvaluateAsync<bool>(
            "element => element.scrollHeight > element.clientHeight"));
        Assert.InRange(await allValues.EvaluateAsync<int>("element => element.offsetHeight"), 1, 30);

        // Interacting with the menu scrollbar must not blur-save and tear down the editor.
        await menu.DispatchEventAsync("pointerdown");
        await pizza.EvaluateAsync("element => element.blur()");
        await page.WaitForTimeoutAsync(50);
        await Assertions.Expect(panel.Locator("tr.row-editor")).ToHaveCountAsync(1);
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await page.EvaluateAsync("window.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))");
        await pizza.FocusAsync();

        await pizza.FillAsync("pe");
        var pepperoni = panel.GetByRole(AriaRole.Option, new() { Name = "2 Pepperoni", Exact = true });
        await Assertions.Expect(pepperoni).ToBeVisibleAsync();
        Assert.True(await pepperoni.EvaluateAsync<bool>(
            """
            element => {
              const bounds = element.getBoundingClientRect();
              const topmost = document.elementFromPoint(
                bounds.left + Math.min(bounds.width / 2, 40), bounds.top + bounds.height / 2);
              return topmost === element || element.contains(topmost);
            }
            """));
        await pepperoni.ClickAsync();
        await Assertions.Expect(pizza).ToHaveValueAsync("2 Pepperoni");
        await pizza.PressAsync("Control+Enter");
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Row 1 updated.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal("2", fixture.Provider.LastWriteValues!["PizzaId"]?.ToString());

        await panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").Nth(2).ClickAsync();
        var promotion = panel.GetByLabel("Promotion", new() { Exact = true });
        await promotion.FillAsync("");
        await promotion.PressAsync("Control+Enter");
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Row 1 updated.", new() { Exact = true }).Last).ToBeVisibleAsync();
        Assert.Equal("2", fixture.Provider.LastWriteValues!["PizzaId"]?.ToString());
        Assert.Null(fixture.Provider.LastWriteValues["Promotion"]);
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "data is not defined", new() { Exact = true })).ToHaveCountAsync(0);

        await panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").Nth(2).ClickAsync();
        await panel.GetByLabel("Promotion", new() { Exact = true }).PressAsync("Tab");
        await Assertions.Expect(panel.GetByLabel("PizzaId", new() { Exact = true }))
            .ToHaveValueAsync("4 #REF!");
        await page.Keyboard.PressAsync("Escape");

        var updatesBeforeDirectKeyEdit = fixture.Provider.Calls.Count(call =>
            call.StartsWith("update dbo.Orders", StringComparison.Ordinal));
        await panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").Nth(1).ClickAsync();
        var directKeyPizza = panel.GetByLabel("PizzaId", new() { Exact = true });
        await directKeyPizza.FillAsync("3");
        await Assertions.Expect(panel.GetByRole(AriaRole.Option, new() { Name = "3 Hawaiian", Exact = true }))
            .ToBeVisibleAsync();
        var updateToasts = page.Locator("#toast-stack").GetByText(
            "Row 1 updated.", new() { Exact = true });
        var updateToastsBeforeDirectKeyEdit = await updateToasts.CountAsync();
        await directKeyPizza.PressAsync("Control+Enter");
        await Assertions.Expect(updateToasts).ToHaveCountAsync(updateToastsBeforeDirectKeyEdit + 1);
        Assert.Equal(updatesBeforeDirectKeyEdit + 1, fixture.Provider.Calls.Count(call =>
            call.StartsWith("update dbo.Orders", StringComparison.Ordinal)));
        Assert.Equal("3", fixture.Provider.LastWriteValues!["PizzaId"]?.ToString());

        var updatesBeforeInvalidEdit = fixture.Provider.Calls.Count(call =>
            call.StartsWith("update dbo.Orders", StringComparison.Ordinal));
        await panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").Nth(1).ClickAsync();
        var invalidPizza = panel.GetByLabel("PizzaId", new() { Exact = true });
        await invalidPizza.FillAsync("not-a-key");
        await invalidPizza.PressAsync("Control+Enter");
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Choose a value for PizzaId from the suggestions.", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator("tr.row-editor")).ToHaveCountAsync(1);
        Assert.Equal(updatesBeforeInvalidEdit, fixture.Provider.Calls.Count(call =>
            call.StartsWith("update dbo.Orders", StringComparison.Ordinal)));

        await client.DeleteAsync(settingUrl);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A heap has no primary key, so the row is addressed by a key the server streams alongside the
    /// rows - here a rowid, which is not one of the columns on screen.
    /// </summary>
    [Fact]
    public async Task Edits_a_row_of_a_table_that_has_no_primary_key()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Heap").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();
        await panel.Locator("tbody tr").Nth(1).Locator("td:not(.row-selector)").First.ClickAsync();
        var name = panel.GetByLabel("Name", new() { Exact = true });
        await name.FillAsync("Grace Hopper");
        await name.PressAsync("Control+Enter");

        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Row 2 updated.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("update dbo.Heap key(rowid) set(Name)", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>A table whose rows cannot be addressed at all stays read-only.</summary>
    [Fact]
    public async Task Offers_no_row_editing_when_the_server_cannot_identify_a_row()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.NoKeys").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();
        await panel.Locator("tbody tr").First.Locator("td:not(.row-selector)").First.ClickAsync();

        await Assertions.Expect(panel.Locator("tr.row-editor")).ToHaveCountAsync(0);
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
    public async Task Sequence_creation_requires_ddl_but_not_ad_hoc_sql_execution()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("#connection-select").SelectOptionAsync("DdlOnly");
        await Assertions.Expect(page.GetByTitle("Create sequence")).ToBeVisibleAsync();
        await page.GetByTitle("dbo.Customers").ClickAsync();
        await Assertions.Expect(ActivePanel(page).GetByTestId("import-data")).ToBeVisibleAsync();
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Sequence_definition_is_read_only_and_restart_is_available()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("summary").Filter(new() { HasText = "Sequences" }).ClickAsync();
        await page.GetByTitle("dbo.OrderNumbers").ClickAsync();
        var panel = ActivePanel(page);
        var editor = panel.GetByTestId("object-definition-editor");

        await Assertions.Expect(editor).Not.ToBeEditableAsync();
        await Assertions.Expect(editor).ToHaveValueAsync(
            new Regex("CREATE SEQUENCE dbo\\.OrderNumbers"));
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Restart…", Exact = true }))
            .ToBeVisibleAsync();
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
        await Assertions.Expect(panel.GetByRole(AriaRole.Cell, new() { Name = "IX_Customers_Name", Exact = true }))
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

    [Fact]
    public async Task Displays_rich_structure_and_uses_dedicated_portable_ddl_routes()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();

        await Assertions.Expect(panel.GetByText("Name COLLATE Latin1_General_CI_AS DESC", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("[Name] IS NOT NULL", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("fill 80 · disabled", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("length([Name]) > 0", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("Name COLLATE NOCASE DESC", new() { Exact = true }))
            .ToBeVisibleAsync();

        var hidden = panel.Locator("details.hidden-columns");
        await Assertions.Expect(hidden.GetByText("SysStart", new() { Exact = true })).Not.ToBeVisibleAsync();
        await hidden.Locator("summary").ClickAsync();
        await Assertions.Expect(hidden.GetByText("SysStart", new() { Exact = true })).ToBeVisibleAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Check", Exact = true }).ClickAsync();
        var checkDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add check constraint" });
        await checkDialog.GetByTestId("check-name").FillAsync("CK_Customers_Id_Positive");
        await checkDialog.GetByTestId("check-expression").FillAsync("[Id] > 0");
        await checkDialog.GetByRole(AriaRole.Button, new() { Name = "Add check", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Check constraint added.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("addCheckConstraint dbo.Customers.CK_Customers_Id_Positive expression=[Id] > 0",
            fixture.Provider.Calls);

        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Unique", Exact = true }).ClickAsync();
        var uniqueDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add unique constraint" });
        await uniqueDialog.GetByTestId("unique-name").FillAsync("UQ_Customers_Name_2");
        await Assertions.Expect(uniqueDialog.GetByLabel("Move key up", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(uniqueDialog.GetByLabel("Move key down", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(uniqueDialog.GetByLabel("Remove key", new() { Exact = true })).ToBeVisibleAsync();
        await uniqueDialog.GetByLabel("Key column").SelectOptionAsync("Name");
        await uniqueDialog.GetByLabel("DESC").CheckAsync();
        await uniqueDialog.GetByRole(AriaRole.Button, new() { Name = "Add unique", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Unique constraint added.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("addUniqueConstraint dbo.Customers.UQ_Customers_Name_2 (Name)", fixture.Provider.Calls);

        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Index", Exact = true }).ClickAsync();
        var indexDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Create index" });
        await indexDialog.GetByTestId("index-name").FillAsync("IX_Customers_Name_2");
        await indexDialog.GetByLabel("Key column").SelectOptionAsync("Name");
        await indexDialog.GetByLabel("DESC").CheckAsync();
        await indexDialog.GetByTestId("index-unique").CheckAsync();
        await indexDialog.GetByTestId("index-filter").FillAsync("[Name] IS NOT NULL");
        await indexDialog.GetByRole(AriaRole.Button, new() { Name = "Create index", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Index created.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains(
            "createIndex dbo.Customers.IX_Customers_Name_2 (Name:DESC) unique=True filter=[Name] IS NOT NULL",
            fixture.Provider.Calls);

        var indexRow = panel.Locator("tr").Filter(new() { HasText = "IX_Customers_Name" });
        await indexRow.GetByLabel("Drop index IX_Customers_Name", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Drop index" })
            .GetByRole(AriaRole.Button, new() { Name = "Drop", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Index dropped.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("dropIndex dbo.Customers.IX_Customers_Name", fixture.Provider.Calls);

        var unnamedCheck = panel.Locator("tr").Filter(new() { HasText = "#0" });
        await unnamedCheck.GetByLabel("Drop check constraint #0", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Drop check constraint" })
            .GetByRole(AriaRole.Button, new() { Name = "Drop", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Check constraint dropped.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("dropCheckConstraint dbo.Customers.#0", fixture.Provider.Calls);

        var uniqueRow = panel.Locator("tr").Filter(new() { HasText = "UQ_Customers_Name" });
        await uniqueRow.GetByLabel("Drop unique constraint UQ_Customers_Name", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Drop unique constraint" })
            .GetByRole(AriaRole.Button, new() { Name = "Drop", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText("Unique constraint dropped.", new() { Exact = true }))
            .ToBeVisibleAsync();
        Assert.Contains("dropUniqueConstraint dbo.Customers.UQ_Customers_Name", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Protects_virtual_and_internal_objects_and_reveals_internal_search_results()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        // Every table the fake provider lists except the internal one, which the tree hides.
        var tables = page.Locator("#tree summary").Filter(new() { HasText = "Tables" });
        await Assertions.Expect(tables).ToContainTextAsync("8");

        await page.GetByTitle("dbo.NoKeys").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await panel.GetByRole(AriaRole.Button, new() { Name = "＋ Foreign key", Exact = true }).ClickAsync();
        var foreignKeyDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add foreign key" });
        var targetOptions = await foreignKeyDialog.Locator("select").First.Locator("option")
            .AllTextContentsAsync();
        Assert.DoesNotContain("dbo.SearchIndex", targetOptions);
        Assert.DoesNotContain("dbo.Customers_fts_data", targetOptions);
        await foreignKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        var virtualTable = page.GetByTitle("dbo.SearchIndex");
        await Assertions.Expect(virtualTable.Locator(".badge")).ToHaveTextAsync("VT");
        await virtualTable.ClickAsync();
        panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "＋ Add column", Exact = true }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Drop table…", Exact = true }))
            .ToBeVisibleAsync();

        var internalGroup = page.Locator("#tree details").Filter(new() { HasText = "Internal" });
        await Assertions.Expect(internalGroup).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(page.GetByTitle("Internal object: Customers_fts_data")).Not.ToBeVisibleAsync();
        await page.Locator("#search").FillAsync("Customers_fts_data");
        var internalObject = page.GetByTitle("Internal object: Customers_fts_data");
        await Assertions.Expect(internalObject).ToBeVisibleAsync();
        await Assertions.Expect(internalObject.Locator(".badge")).ToHaveTextAsync("I");
        await internalObject.ClickAsync();
        panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "＋ Row", Exact = true }))
            .ToHaveCountAsync(0);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "＋ Add column", Exact = true }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Drop table…", Exact = true }))
            .ToHaveCountAsync(0);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Switching_connection_keeps_tabs_open_and_bound_to_their_own_database()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 1 AS Answer");

        var queryRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/query", StringComparison.Ordinal))
            {
                queryRequests.Add(request.Url);
            }
        };

        await page.Locator("#connection-select").SelectOptionAsync("SQLite");
        await Assertions.Expect(page.Locator("#connection-select")).ToHaveValueAsync("SQLite");

        // The tab survives the switch and shows the connection it still runs on.
        var tab = page.Locator("#tabbar .tab");
        await Assertions.Expect(tab).ToHaveCountAsync(1);
        await Assertions.Expect(tab.GetByTestId("tab-scope")).ToHaveTextAsync("Main / FakeDb");

        var panel = ActivePanel(page);
        await panel.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("1 ms");

        Assert.All(queryRequests, url => Assert.Contains("/connections/Main/databases/FakeDb/query", url));
        Assert.NotEmpty(queryRequests);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Opens_each_published_endpoint_in_its_own_request_tab()
    {
        using var client = new HttpClient { BaseAddress = fixture.BaseAddress };
        foreach (var name in new[] { "Tab one", "Tab two" })
        {
            using var publish = await client.PostAsJsonAsync("/gridlet/api/published", new
            {
                name,
                method = "GET",
                route = name.Replace(' ', '-').ToLowerInvariant(),
                connectionName = "Main",
                database = "FakeDb",
                sql = "SELECT 42",
            });
            publish.EnsureSuccessStatusCode();
        }

        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        await page.Locator("#apis-btn").ClickAsync();

        var rows = page.Locator("#panels tr");
        var tabs = page.Locator("#tabbar .tab");
        var apisTab = tabs.Filter(new() { HasText = "Published APIs" });
        await rows.Filter(new() { HasText = "Tab one" }).GetByTestId("open-api-request").ClickAsync();
        await apisTab.ClickAsync();
        await rows.Filter(new() { HasText = "Tab two" }).GetByTestId("open-api-request").ClickAsync();

        // Both requests stay open next to the endpoint list instead of replacing each other.
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        await Assertions.Expect(tabs.Filter(new() { HasText = "Tab one" })).ToHaveCountAsync(1);
        await Assertions.Expect(tabs.Filter(new() { HasText = "Tab two" })).ToHaveCountAsync(1);
        await Assertions.Expect(ActivePanel(page).Locator(".api-preview-address"))
            .ToHaveValueAsync(new Regex("/gridlet/pub/tab-two$"));

        // The full API preview uses the same raw/pretty JSON presentation as Ask.
        var preview = ActivePanel(page);
        await preview.GetByRole(AriaRole.Button, new() { Name = "Go" }).ClickAsync();
        await Assertions.Expect(preview.Locator(".api-response-status"))
            .ToHaveTextAsync(new Regex("^200"));
        await Assertions.Expect(preview.Locator(".api-code-content .json-key").Nth(0)).ToBeVisibleAsync();
        var rawResponse = preview.GetByRole(AriaRole.Button, new() { Name = "Raw" });
        var prettyResponse = preview.GetByRole(AriaRole.Button, new() { Name = "Pretty" });
        await rawResponse.ClickAsync();
        await Assertions.Expect(rawResponse).ToHaveAttributeAsync("aria-pressed", "true");
        await prettyResponse.ClickAsync();
        await Assertions.Expect(prettyResponse).ToHaveAttributeAsync("aria-pressed", "true");

        // Re-opening the same endpoint focuses its tab rather than adding another.
        await apisTab.ClickAsync();
        await rows.Filter(new() { HasText = "Tab one" }).GetByTestId("open-api-request").ClickAsync();
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        await Assertions.Expect(ActivePanel(page).Locator(".api-preview-address"))
            .ToHaveValueAsync(new Regex("/gridlet/pub/tab-one$"));

        // An empty request tab is always available for ad-hoc calls.
        await apisTab.ClickAsync();
        await page.GetByTestId("new-api-request").ClickAsync();
        await Assertions.Expect(tabs).ToHaveCountAsync(4);
        await Assertions.Expect(ActivePanel(page).Locator(".api-preview-address")).ToHaveValueAsync("");
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// While one endpoint is open for editing, the actions on screen should be the ones for that
    /// endpoint. Starting a blank request is a list-view action and steps aside for them.
    /// </summary>
    [Fact]
    public async Task Editing_an_endpoint_offers_run_beside_save_instead_of_a_new_request()
    {
        using var client = new HttpClient { BaseAddress = fixture.BaseAddress };
        using var publish = await client.PostAsJsonAsync("/gridlet/api/published", new
        {
            name = "Editable",
            method = "GET",
            route = "editable",
            connectionName = "Main",
            database = "FakeDb",
            sql = "SELECT 42",
        });
        publish.EnsureSuccessStatusCode();

        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        await page.Locator("#apis-btn").ClickAsync();

        var newRequest = page.GetByTestId("new-api-request");
        var run = page.GetByTestId("run-api-endpoint");
        await Assertions.Expect(newRequest).ToBeVisibleAsync();
        await Assertions.Expect(run).ToHaveCountAsync(0);

        await page.Locator("#panels tr").Filter(new() { HasText = "Editable" })
            .Locator("button[title='Edit endpoint inline']").ClickAsync();

        await Assertions.Expect(run).ToBeVisibleAsync();
        await Assertions.Expect(newRequest).ToBeHiddenAsync();

        // Untouched, there is nothing to save, so Run just runs.
        await Assertions.Expect(run).ToHaveTextAsync("Run");
        await run.ClickAsync();
        await Assertions.Expect(ActivePanel(page).Locator(".api-preview-address"))
            .ToHaveValueAsync(new Regex("/gridlet/pub/editable$"));

        // Leaving the editor brings the list-view action back.
        await page.Locator("#tabbar .tab").Filter(new() { HasText = "Published APIs" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(newRequest).ToBeVisibleAsync();
        await Assertions.Expect(run).ToHaveCountAsync(0);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A request always hits the stored endpoint, so edits have to be saved before they can be
    /// tried. The button says which of the two it is about to do rather than running the old
    /// version, and it follows the form as it is edited.
    /// </summary>
    [Fact]
    public async Task Editing_an_endpoint_turns_run_into_save_and_run()
    {
        using var client = new HttpClient { BaseAddress = fixture.BaseAddress };
        using var publish = await client.PostAsJsonAsync("/gridlet/api/published", new
        {
            name = "Rerouted",
            method = "GET",
            route = "rerouted",
            connectionName = "Main",
            database = "FakeDb",
            sql = "SELECT 42",
        });
        publish.EnsureSuccessStatusCode();

        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");
        await page.Locator("#apis-btn").ClickAsync();
        await page.Locator("#panels tr").Filter(new() { HasText = "Rerouted" })
            .Locator("button[title='Edit endpoint inline']").ClickAsync();

        var run = page.GetByTestId("run-api-endpoint");
        await Assertions.Expect(run).ToHaveTextAsync("Run");

        var route = ActivePanel(page).Locator(".inline-form input[type=text]").Nth(1);
        await route.FillAsync("rerouted-v2");
        await Assertions.Expect(run).ToHaveTextAsync("Save and run");

        // Reverting by hand leaves nothing to save, so the button goes back on its own.
        await route.FillAsync("rerouted");
        await Assertions.Expect(run).ToHaveTextAsync("Run");

        await route.FillAsync("rerouted-v2");
        await run.ClickAsync();

        // The change is saved first, and the request tab opens on the route that was just stored.
        await Assertions.Expect(ActivePanel(page).Locator(".api-preview-address"))
            .ToHaveValueAsync(new Regex("/gridlet/pub/rerouted-v2$"));
        var endpoints = await client.GetStringAsync("/gridlet/api/published");
        Assert.Contains("rerouted-v2", endpoints, StringComparison.Ordinal);
        browserPage.AssertNoUnexpectedErrors();
    }

    private static ILocator ActivePanel(IPage page) => page.Locator("#panels .panel:not([hidden])");

    private static async Task ClickQueryActionAsync(ILocator panel, string testId)
    {
        var control = panel.GetByTestId(testId);
        await panel.EvaluateAsync("""
            element => new Promise(resolve => requestAnimationFrame(
                () => requestAnimationFrame(resolve)))
            """);
        if (await control.IsVisibleAsync())
        {
            await control.ClickAsync();
            return;
        }

        await panel.GetByTestId("query-toolbar")
            .GetByRole(AriaRole.Button, new() { Name = "More query actions" })
            .ClickAsync();
        await control.ClickAsync();
    }

    /// <summary>
    /// WITHOUT ROWID and STRICT change what a table is, so the designer offers them where the
    /// provider has them and leaves them out where it does not.
    /// </summary>
    [Fact]
    public async Task Offers_table_options_only_where_the_provider_has_them()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("Create table").ClickAsync();
        var sqlServerPanel = ActivePanel(page);
        await Assertions.Expect(sqlServerPanel.GetByTestId("table-option-strict")).ToHaveCountAsync(0);

        await page.Locator("#connection-select").SelectOptionAsync("SQLite");
        await page.GetByTitle("Create table").ClickAsync();
        var sqlitePanel = ActivePanel(page);
        await Assertions.Expect(sqlitePanel.GetByTestId("table-option-strict")).ToBeVisibleAsync();
        await Assertions.Expect(sqlitePanel.GetByTestId("table-option-without-rowid")).ToBeVisibleAsync();
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// Scripting is the way out of anything the designer will not do, so the script has to land
    /// somewhere it can be read and edited before it runs.
    /// </summary>
    [Fact]
    public async Task Scripts_an_object_into_a_query_tab()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await panel.GetByTestId("script-object").ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Script dbo.Customers" });
        await dialog.GetByTestId("script-drop").CheckAsync();
        await dialog.GetByTestId("script-data").CheckAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Script", Exact = true }).ClickAsync();

        var editor = ActivePanel(page).GetByTestId("sql-editor");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        var sql = await editor.InputValueAsync();
        Assert.Contains("DROP TABLE dbo.Customers;", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE VIEW dbo.Customers", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO dbo.Customers", sql, StringComparison.Ordinal);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Renames_an_object_and_empties_a_table()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();

        await panel.GetByTestId("empty-table").ClickAsync();
        var emptyDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Empty table" });
        await Assertions.Expect(emptyDialog).ToContainTextAsync("cannot be undone");
        await emptyDialog.GetByRole(AriaRole.Button, new() { Name = "Delete all rows", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "dbo.Customers emptied.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("truncate dbo.Customers", fixture.Provider.Calls);

        await panel.GetByRole(AriaRole.Button, new() { Name = "Structure", Exact = true }).ClickAsync();
        await panel.GetByTestId("rename-object").ClickAsync();
        var renameDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Rename dbo.Customers" });
        await Assertions.Expect(renameDialog).ToContainTextAsync("are not updated");
        await renameDialog.GetByTestId("rename-name").FillAsync("Clients");
        await renameDialog.GetByRole(AriaRole.Button, new() { Name = "Rename", Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("#toast-stack").GetByText(
            "Renamed to Clients.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Contains("renameObject Table dbo.Customers -> Clients", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A filter has to reach the database: filtering the page already fetched would only ever search
    /// the rows on screen.
    /// </summary>
    [Fact]
    public async Task Filters_table_data_in_the_database()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        var dataRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/data/stream", StringComparison.Ordinal)) dataRequests.Add(request.Url);
        };
        await page.GotoAsync("/gridlet/");

        await page.GetByTitle("dbo.Customers").ClickAsync();
        var panel = ActivePanel(page);
        await Assertions.Expect(panel.GetByText("2 row(s)", new() { Exact = true })).ToBeVisibleAsync();

        await panel.GetByTestId("add-filter").ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Filter rows" });
        await dialog.GetByLabel("Filter column").SelectOptionAsync("Name");
        await dialog.GetByLabel("Filter operator").SelectOptionAsync("contains");
        await dialog.GetByLabel("Filter value").FillAsync("ada");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();

        await Assertions.Expect(panel.GetByTestId("filter-chip")).ToHaveTextAsync("Name contains ada×");
        Assert.Contains(dataRequests, url => Uri.UnescapeDataString(url)
            .Contains("""filter=[{"column":"Name","operator":"contains","value":"ada"}]""", StringComparison.Ordinal));

        await panel.GetByTestId("filter-chip").GetByRole(AriaRole.Button).ClickAsync();
        await Assertions.Expect(panel.GetByTestId("filter-chip")).ToHaveCountAsync(0);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// "Why is this slow" is the question results cannot answer. The plan renders as a tree with the
    /// operator, what it touches, the numbers that matter, and any warning attached to it.
    /// </summary>
    [Fact]
    public async Task Shows_an_execution_plan_for_a_query()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 1");
        var panel = ActivePanel(page);

        await panel.GetByTestId("query-plan-estimated").ClickAsync();

        var plan = panel.GetByTestId("query-plan");
        await Assertions.Expect(plan).ToBeVisibleAsync();
        await Assertions.Expect(plan).ToContainTextAsync("Clustered Index Scan");
        await Assertions.Expect(plan).ToContainTextAsync("Customers.PK_Customers");
        await Assertions.Expect(plan).ToContainTextAsync("Missing index on Customers (Name)");
        await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("Estimated plan");

        await panel.GetByTestId("query-plan-actual").ClickAsync();
        await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("Actual plan");
        await Assertions.Expect(panel.GetByText("logical reads 3")).ToBeVisibleAsync();
        Assert.Contains("plan.actual SELECT 1", fixture.Provider.Calls);

        var editor = panel.GetByTestId("sql-editor");
        await editor.FillAsync("DELETE FROM Customers");
        var callsBeforeWarning = fixture.Provider.Calls.Count(call => call.StartsWith("plan.actual "));
        await panel.GetByTestId("query-plan-actual").ClickAsync();
        var warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Run query without WHERE?" });
        await Assertions.Expect(warning).ToBeVisibleAsync();
        Assert.Equal(callsBeforeWarning,
            fixture.Provider.Calls.Count(call => call.StartsWith("plan.actual ")));
        await warning.GetByRole(AriaRole.Button, new() { Name = "Run anyway", Exact = true }).ClickAsync();
        await Assertions.Expect(panel.GetByTestId("query-status")).ToHaveTextAsync("Actual plan");
        Assert.Contains("plan.actual DELETE FROM Customers", fixture.Provider.Calls);

        await ClickQueryActionAsync(panel, "query-history");
        var history = page.GetByRole(AriaRole.Dialog, new() { Name = "Query history" });
        await Assertions.Expect(history.GetByTestId("query-history-item").First)
            .ToContainTextAsync("DELETE FROM Customers");
        await Assertions.Expect(history.GetByTestId("query-history-item").First)
            .ToContainTextAsync("Succeeded");
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A procedure used to open as the bare text <c>EXEC dbo.Proc;</c>. It now offers a form for its
    /// arguments, and what runs is a script the person can see and keep.
    /// </summary>
    [Fact]
    public async Task Executes_a_stored_procedure_with_arguments()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await page.GotoAsync("/gridlet/");

        await page.Locator("summary").Filter(new() { HasText = "Stored procedures" }).ClickAsync();
        await page.GetByTitle("dbo.RefreshOrders").ClickAsync();
        var panel = ActivePanel(page);
        await panel.GetByTestId("execute-routine").ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Execute dbo.RefreshOrders" });
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        // The return value is not something to fill in, and the output parameter starts unset.
        await Assertions.Expect(dialog.GetByLabel("@ReturnValue value")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog.GetByLabel("@RowsChanged argument")).ToHaveValueAsync("omit");
        await dialog.GetByLabel("@Since value").FillAsync("2026-01-01");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();

        var queryPanel = ActivePanel(page);
        await Assertions.Expect(queryPanel.GetByTestId("sql-editor"))
            .ToHaveValueAsync("EXEC dbo.RefreshOrders @Since = 2026-01-01;");
        await Assertions.Expect(queryPanel.GetByTestId("query-status")).ToContainTextAsync("ms");
        Assert.Contains("script dbo.RefreshOrders (@Since = 2026-01-01)", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    /// <summary>
    /// A pinned session is the only way an explicit transaction survives from one execution to the
    /// next, so the toolbar has to show whether one is open and let the person end it.
    /// </summary>
    [Fact]
    public async Task Runs_a_transaction_across_executions_in_a_pinned_session()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");
        var panel = ActivePanel(page);
        var state = panel.GetByTestId("session-state");

        await ClickQueryActionAsync(panel, "session-toggle");
        await Assertions.Expect(state).ToHaveTextAsync("session - no transaction");
        await Assertions.Expect(panel.GetByTestId("transaction-commit")).ToBeDisabledAsync();

        await ClickQueryActionAsync(panel, "transaction-begin");
        await Assertions.Expect(state).ToHaveTextAsync("transaction open");
        await page.GetByTestId("query-run").ClickAsync();
        await Assertions.Expect(panel.GetByTestId("query-status")).Not.ToHaveTextAsync("Running…");

        // The transaction is still open after the execution: that is the whole point of a session.
        await Assertions.Expect(state).ToHaveTextAsync("transaction open");
        Assert.Contains("session.query SELECT 42", fixture.Provider.Calls);

        await ClickQueryActionAsync(panel, "transaction-commit");
        await Assertions.Expect(state).ToHaveTextAsync("session - no transaction");
        Assert.Contains("session.commit", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

    [Fact]
    public async Task Closing_a_tab_with_an_open_transaction_asks_first_and_rolls_back()
    {
        await using var browserPage = await fixture.NewPageAsync();
        var page = browserPage.Page;
        await OpenQueryAsync(page, "SELECT 42");
        var panel = ActivePanel(page);
        await ClickQueryActionAsync(panel, "session-toggle");
        await ClickQueryActionAsync(panel, "transaction-begin");
        await Assertions.Expect(panel.GetByTestId("session-state")).ToHaveTextAsync("transaction open");

        await page.Locator("#tabbar .tab.active .tab-close").ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Transaction still open" });
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep tab open", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#tabbar .tab")).ToHaveCountAsync(1);

        await page.Locator("#tabbar .tab.active .tab-close").ClickAsync();
        var sessionClosed = page.WaitForResponseAsync(response =>
            response.Request.Method == "DELETE"
            && response.Url.Contains("/api/sessions/", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Transaction still open" })
            .GetByRole(AriaRole.Button, new() { Name = "Roll back and close", Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("#tabbar .tab")).ToHaveCountAsync(0);
        await sessionClosed;
        Assert.Contains("session.rollback", fixture.Provider.Calls);
        browserPage.AssertNoUnexpectedErrors();
    }

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
