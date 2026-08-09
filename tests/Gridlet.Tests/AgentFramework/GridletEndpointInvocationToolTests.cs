using Gridlet.Abstractions;
using Gridlet.AgentFramework;
using Gridlet.Models;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

/// <summary>
/// Covers the tool that lets a turn call this installation's own published endpoints, and the
/// environment facts that stop the agent inventing addresses for them.
/// </summary>
public sealed class GridletEndpointInvocationToolTests
{
    private static readonly GridletAgentEnvironment Environment =
        new("https://localhost:5088/", "/gridlet");

    private static readonly GridletPublishedEndpointInvocation Response = new(
        Succeeded: true,
        Method: "GET",
        Url: "https://localhost:5088/gridlet/pub/customers",
        StatusCode: 200,
        ContentType: "application/json",
        Body: """[{"Id":1}]""",
        Truncated: false,
        ElapsedMilliseconds: 12);

    [Fact]
    public async Task Invoking_an_endpoint_returns_the_real_response_to_the_model()
    {
        var invoker = new RecordingInvoker(new GridletPublishedEndpointInvocation(
            Succeeded: true,
            Method: "GET",
            Url: "https://localhost:5088/gridlet/pub/customers?country=Germany",
            StatusCode: 200,
            ContentType: "application/json",
            Body: """[{"Id":1,"Country":"Germany"}]""",
            Truncated: false,
            ElapsedMilliseconds: 12));

        var result = await InvokeAsync(invoker, new AIFunctionArguments
        {
            ["name"] = "customers",
            ["parameters"] = """{"country":"Germany"}""",
        });

        Assert.Equal("customers", invoker.Name);
        Assert.Equal("Germany", invoker.Query["country"]);
        Assert.Contains("\"status\":200", result, StringComparison.Ordinal);
        Assert.Contains("Germany", result, StringComparison.Ordinal);
        Assert.Contains("pub/customers", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Calling an endpoint sits behind the API grant, and nothing else opens it. Sharing schema and
    /// even data is not enough, so the person who ticked "Data" for the query tool has not silently
    /// also handed over every published endpoint.
    /// </summary>
    [Fact]
    public async Task Invoking_an_endpoint_needs_the_api_grant()
    {
        var invoker = new RecordingInvoker(null!);

        var result = await InvokeAsync(
            invoker,
            new AIFunctionArguments { ["name"] = "customers" },
            shared: new GridletAgentAccess(Schema: true, Data: true, Api: false));

        Assert.Null(invoker.Name);
        Assert.Contains("access_not_shared", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// The API grant stands on its own: an endpoint returns rows, and the person was told so when
    /// they ticked it, so it does not additionally require the data grant.
    /// </summary>
    [Fact]
    public async Task Invoking_an_endpoint_does_not_additionally_need_the_data_grant()
    {
        var invoker = new RecordingInvoker(Response);

        var result = await InvokeAsync(
            invoker,
            new AIFunctionArguments { ["name"] = "customers" },
            shared: new GridletAgentAccess(Schema: false, Data: false, Api: true));

        Assert.Equal("customers", invoker.Name);
        Assert.DoesNotContain("access_not_shared", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_from_the_invoker_reaches_the_model_as_a_recoverable_error()
    {
        var invoker = new RecordingInvoker(new GridletPublishedEndpointInvocation(
            Succeeded: false,
            Method: "GET",
            Url: string.Empty,
            StatusCode: null,
            ContentType: null,
            Body: null,
            Truncated: false,
            ElapsedMilliseconds: 0,
            ErrorCode: "method_not_invocable",
            ErrorMessage: "'archive' is a POST endpoint."));

        var result = await InvokeAsync(
            invoker, new AIFunctionArguments { ["name"] = "archive" });

        Assert.Contains("method_not_invocable", result, StringComparison.Ordinal);
        Assert.Contains("\"recoverable\":true", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parameters_that_are_not_a_flat_json_object_are_rejected_before_any_call()
    {
        var invoker = new RecordingInvoker(null!);

        var notObject = await InvokeAsync(invoker, new AIFunctionArguments
        {
            ["name"] = "customers",
            ["parameters"] = "\"Germany\"",
        });
        var nested = await InvokeAsync(invoker, new AIFunctionArguments
        {
            ["name"] = "customers",
            ["parameters"] = """{"filter":{"country":"Germany"}}""",
        });

        Assert.Null(invoker.Name);
        Assert.Contains("invalid_parameters", notObject, StringComparison.Ordinal);
        Assert.Contains("invalid_parameters", nested, StringComparison.Ordinal);
    }

    /// <summary>
    /// The agent used to reach for the placeholder address in its own documentation. The turn now
    /// carries the address the person is actually looking at.
    /// </summary>
    [Fact]
    public void Instructions_carry_the_address_this_installation_answers_on()
    {
        var instructions = GridletAgentFrameworkService.CreateInstructions(
            "Base.",
            new GridletDatabaseSystemInfo("SQLite", "3.45"),
            new GridletAgentAccess(true, true),
            new GridletAgentAccess(true, true),
            Environment);

        Assert.Contains("https://localhost:5088/", instructions, StringComparison.Ordinal);
        Assert.Contains(
            "https://localhost:5088/gridlet/pub/{route}", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", instructions, StringComparison.Ordinal);
    }

    /// <summary>
    /// The published segment is the host's choice, so the agent is told the real one. Without this
    /// it would build every URL from the documented default and hand people dead links.
    /// </summary>
    [Fact]
    public void The_configured_published_segment_reaches_the_agent_instructions()
    {
        var instructions = GridletAgentFrameworkService.CreateInstructions(
            "Base.",
            new GridletDatabaseSystemInfo("SQLite", "3.45"),
            new GridletAgentAccess(true, true),
            new GridletAgentAccess(true, true),
            new GridletAgentEnvironment("https://localhost:5088/", "/gridlet", "endpoints"));

        Assert.Contains(
            "https://localhost:5088/gridlet/endpoints/{route}",
            instructions,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/gridlet/pub/", instructions, StringComparison.Ordinal);
    }

    /// <summary>A host that supplies no address must not make the agent guess one.</summary>
    [Fact]
    public void Instructions_state_no_address_when_the_host_supplied_none()
    {
        var instructions = GridletAgentFrameworkService.CreateInstructions(
            "Base.",
            new GridletDatabaseSystemInfo("SQLite"),
            new GridletAgentAccess(true, true),
            new GridletAgentAccess(true, true));

        Assert.DoesNotContain("Base address", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", instructions, StringComparison.Ordinal);
    }

    private static async Task<string> InvokeAsync(
        IGridletPublishedEndpointInvoker invoker,
        AIFunctionArguments arguments,
        GridletAgentAccess? shared = null)
    {
        var function = GridletDatabaseAgentToolsTests
            .CreateTools(
                shared ?? new GridletAgentAccess(true, true, true),
                endpointInvoker: invoker,
                environment: Environment)
            .Create()
            .OfType<AIFunction>()
            .Single(tool => tool.Name == "invoke_published_api_endpoint");

        return (await function.InvokeAsync(arguments))?.ToString() ?? string.Empty;
    }

    private sealed class RecordingInvoker(GridletPublishedEndpointInvocation result)
        : IGridletPublishedEndpointInvoker
    {
        public string? Name { get; private set; }

        public IReadOnlyDictionary<string, string?> Query { get; private set; } =
            new Dictionary<string, string?>();

        public Task<GridletPublishedEndpointInvocation> InvokeAsync(
            string name,
            IReadOnlyDictionary<string, string?> query,
            GridletAgentUserContext user,
            CancellationToken cancellationToken = default)
        {
            Name = name;
            Query = query;
            return Task.FromResult(result);
        }
    }
}
