using System.Collections.Concurrent;
using System.Text.Json;
using Gridlet.Models;
using Gridlet.AgentFramework;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletAgentFrameworkServiceTests
{
    [Fact]
    public void Drains_all_pending_tool_events()
    {
        var pending = new ConcurrentQueue<GridletAgentStreamEvent>();
        pending.Enqueue(new GridletAgentStreamEvent("tool-result", "one", "first"));
        pending.Enqueue(new GridletAgentStreamEvent("tool-result", "two", "second"));

        var drained = GridletAgentFrameworkService.DrainPendingToolEvents(pending).ToArray();

        Assert.Equal(2, drained.Length);
        Assert.Equal("first", drained[0].Name);
        Assert.Equal("second", drained[1].Name);
        Assert.Empty(pending);
    }

    [Fact]
    public void Ignores_non_object_function_results_from_non_codex_providers()
    {
        var functionResult = new FunctionResultContent(
            "call-id",
            JsonSerializer.SerializeToElement("tool result"));

        var recognized = GridletAgentFrameworkService.TryReadFailedToolResult(
            functionResult,
            out var toolName,
            out var result);

        Assert.False(recognized);
        Assert.Null(toolName);
        Assert.Null(result);
    }

    [Fact]
    public void Reads_failed_codex_dynamic_tool_results()
    {
        var functionResult = new FunctionResultContent(
            "call-id",
            JsonSerializer.SerializeToElement(new
            {
                type = "dynamicToolCall",
                tool = "describe_table",
                success = false,
                contentItems = new[] { new { text = "Tool failed." } },
            }));

        var recognized = GridletAgentFrameworkService.TryReadFailedToolResult(
            functionResult,
            out var toolName,
            out var result);

        Assert.True(recognized);
        Assert.Equal("describe_table", toolName);
        Assert.Equal("Tool failed.", result);
    }

    [Fact]
    public void Reads_provider_neutral_failed_tool_results()
    {
        var functionResult = new FunctionResultContent(
            "call-id",
            new AgentToolInvocationResult(
                "describe_table", Success: false, Result: "Provider tool failed."));

        var recognized = GridletAgentFrameworkService.TryReadFailedToolResult(
            functionResult,
            out var toolName,
            out var result);

        Assert.True(recognized);
        Assert.Equal("describe_table", toolName);
        Assert.Equal("Provider tool failed.", result);
    }
}
