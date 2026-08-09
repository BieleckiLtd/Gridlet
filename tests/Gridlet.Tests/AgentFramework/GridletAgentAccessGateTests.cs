using Gridlet.AgentFramework;
using Gridlet.Models;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletAgentAccessGateTests
{
    private static readonly GridletAgentUserContext Anonymous =
        new(Subject: null, DisplayName: null, IsAuthenticated: false);

    [Fact]
    public async Task Granting_a_scope_opens_it_for_the_rest_of_the_turn()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(registry, events, new GridletAgentAccess(Schema: true, Data: false));

        var request = gate.RequestAsync(
            GridletAgentAccessScope.Data, "Count the orders.", CancellationToken.None);
        var requestId = await WaitForRequestIdAsync(events);

        Assert.False(gate.IsShared(GridletAgentAccessScope.Data));
        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: true, Anonymous));
        Assert.Equal(GridletAgentAccessRequestOutcome.Granted, await request);
        Assert.True(gate.IsShared(GridletAgentAccessScope.Data));
        Assert.Contains(events, item => item.Type == "permission-resolved");
    }

    [Fact]
    public async Task Requesting_api_access_reports_the_updated_shared_state()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(
            registry,
            events,
            GridletAgentAccess.None,
            hostAllows: new GridletAgentAccess(Schema: true, Data: true, Api: true));
        var function = GridletDatabaseAgentToolsTests.CreateTools(gate: gate)
            .Create()
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "request_database_access");

        var invocation = function.InvokeAsync(new AIFunctionArguments
        {
            ["scope"] = "api",
            ["reason"] = "Call the published customers endpoint.",
        });
        var requestId = await WaitForRequestIdAsync(events);
        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Api, granted: true, Anonymous));

        var result = (await invocation)?.ToString();

        Assert.Contains("\"shared\":{\"schema\":false,\"data\":false,\"api\":true}", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser keys the prompt card off the scope name. An ordinal there renders no card at all,
    /// so the person never sees the question and the waiting turn only ends when the prompt expires.
    /// </summary>
    [Fact]
    public async Task The_prompt_names_its_scope()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(registry, events, new GridletAgentAccess(Schema: false, Data: false));

        var request = gate.RequestAsync(
            GridletAgentAccessScope.Schema, "List the tables.", CancellationToken.None);
        var requestId = await WaitForRequestIdAsync(events);

        var prompt = events.First(item => item.Type == "permission-request");
        using (var document = System.Text.Json.JsonDocument.Parse(prompt.Content!))
        {
            Assert.Equal("schema", document.RootElement.GetProperty("scope").GetString());
        }

        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Schema, granted: true, Anonymous));
        await request;

        var resolved = events.First(item => item.Type == "permission-resolved");
        using (var document = System.Text.Json.JsonDocument.Parse(resolved.Content!))
        {
            Assert.Equal("schema", document.RootElement.GetProperty("scope").GetString());
        }
    }

    [Fact]
    public async Task A_denied_request_leaves_the_scope_closed()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(registry, events, new GridletAgentAccess(Schema: true, Data: false));

        var request = gate.RequestAsync(
            GridletAgentAccessScope.Data, "Read every row.", CancellationToken.None);
        var requestId = await WaitForRequestIdAsync(events);
        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: false, Anonymous));

        Assert.Equal(GridletAgentAccessRequestOutcome.Denied, await request);
        Assert.False(gate.IsShared(GridletAgentAccessScope.Data));
    }

    [Fact]
    public async Task A_scope_the_host_disabled_is_never_put_to_the_person()
    {
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(
            new GridletAgentPermissionRegistry(),
            events,
            new GridletAgentAccess(Schema: true, Data: false),
            hostAllows: new GridletAgentAccess(Schema: true, Data: false));

        var outcome = await gate.RequestAsync(
            GridletAgentAccessScope.Data, "Please.", CancellationToken.None);

        Assert.Equal(GridletAgentAccessRequestOutcome.NotConfigured, outcome);
        Assert.Empty(events);
    }

    [Fact]
    public async Task An_unanswered_request_expires_as_a_denial()
    {
        var gate = CreateGate(
            new GridletAgentPermissionRegistry(),
            events: [],
            new GridletAgentAccess(Schema: true, Data: false),
            promptTimeout: TimeSpan.FromMilliseconds(50));

        var outcome = await gate.RequestAsync(
            GridletAgentAccessScope.Data, "Waiting.", CancellationToken.None);

        Assert.Equal(GridletAgentAccessRequestOutcome.TimedOut, outcome);
        Assert.False(gate.IsShared(GridletAgentAccessScope.Data));
    }

    [Fact]
    public async Task An_answer_from_a_different_owner_is_ignored()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = new GridletAgentAccessGate(
            new GridletAgentAccess(Schema: true, Data: true),
            new GridletAgentAccess(Schema: true, Data: false),
            new GridletAgentUserContext("owner", "Owner", IsAuthenticated: true),
            registry,
            TimeSpan.FromSeconds(30),
            streamEvent =>
            {
                events.Add(streamEvent);
                return ValueTask.CompletedTask;
            });

        var request = gate.RequestAsync(
            GridletAgentAccessScope.Data, "Read orders.", CancellationToken.None);
        var requestId = await WaitForRequestIdAsync(events);

        Assert.False(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: true,
            new GridletAgentUserContext("intruder", "Intruder", IsAuthenticated: true)));
        // The scope for the request matters too: a schema answer cannot settle a data request.
        Assert.False(registry.TryResolve(
            requestId, GridletAgentAccessScope.Schema, granted: true,
            new GridletAgentUserContext("owner", "Owner", IsAuthenticated: true)));
        Assert.False(gate.IsShared(GridletAgentAccessScope.Data));

        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: true,
            new GridletAgentUserContext("owner", "Owner", IsAuthenticated: true)));
        Assert.Equal(GridletAgentAccessRequestOutcome.Granted, await request);
    }

    [Fact]
    public async Task An_answered_request_cannot_be_answered_twice()
    {
        var registry = new GridletAgentPermissionRegistry();
        var events = new List<GridletAgentStreamEvent>();
        var gate = CreateGate(registry, events, new GridletAgentAccess(Schema: true, Data: false));

        var request = gate.RequestAsync(
            GridletAgentAccessScope.Data, "Read orders.", CancellationToken.None);
        var requestId = await WaitForRequestIdAsync(events);

        Assert.True(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: false, Anonymous));
        await request;
        Assert.False(registry.TryResolve(
            requestId, GridletAgentAccessScope.Data, granted: true, Anonymous));
        Assert.False(gate.IsShared(GridletAgentAccessScope.Data));
    }

    [Fact]
    public async Task A_query_without_shared_data_reports_a_recoverable_error_and_reads_nothing()
    {
        var gate = CreateGate(
            new GridletAgentPermissionRegistry(),
            events: [],
            new GridletAgentAccess(Schema: true, Data: false));
        var function = GridletDatabaseAgentToolsTests.CreateTools(gate: gate)
            .Create()
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "execute_read_only_query");

        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["sql"] = "SELECT 1;" });

        var text = result?.ToString();
        Assert.Contains("access_not_shared", text, StringComparison.Ordinal);
        Assert.Contains("\"recoverable\":true", text, StringComparison.Ordinal);
        Assert.Contains("request_database_access", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gridlet_guide_topics_are_available_without_sharing_any_database_context()
    {
        var gate = CreateGate(
            new GridletAgentPermissionRegistry(), events: [], GridletAgentAccess.None);
        var function = GridletDatabaseAgentToolsTests.CreateTools(gate: gate)
            .Create()
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "get_gridlet_guide");

        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["topic"] = "published-api-parameters" });

        Assert.Contains("SQL injection", result?.ToString(), StringComparison.Ordinal);
    }

    private static GridletAgentAccessGate CreateGate(
        GridletAgentPermissionRegistry registry,
        List<GridletAgentStreamEvent> events,
        GridletAgentAccess shared,
        GridletAgentAccess? hostAllows = null,
        TimeSpan? promptTimeout = null)
        => new(
            hostAllows ?? new GridletAgentAccess(Schema: true, Data: true),
            shared,
            Anonymous,
            registry,
            promptTimeout ?? TimeSpan.FromSeconds(30),
            streamEvent =>
            {
                events.Add(streamEvent);
                return ValueTask.CompletedTask;
            });

    /// <summary>
    /// The prompt is emitted from the waiting call, so the test reads the identifier off the
    /// stream exactly as the browser would.
    /// </summary>
    private static async Task<string> WaitForRequestIdAsync(List<GridletAgentStreamEvent> events)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var prompt = events.FirstOrDefault(item => item.Type == "permission-request");
            if (prompt?.Content is { } content)
            {
                using var document = System.Text.Json.JsonDocument.Parse(content);
                return document.RootElement.GetProperty("requestId").GetString()!;
            }
            await Task.Delay(10);
        }

        throw new InvalidOperationException("No access prompt was emitted.");
    }
}
