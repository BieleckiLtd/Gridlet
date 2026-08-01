using System.Text.Json;
using Gridlet.AgentFramework;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletAgentFrameworkServiceTests
{
    [Fact]
    public void Ignores_non_object_function_results_from_non_codex_providers()
    {
        var functionResult = new FunctionResultContent(
            "call-id",
            JsonSerializer.SerializeToElement("tool result"));

        var recognized = GridletAgentFrameworkService.TryReadFailedCodexToolResult(
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

        var recognized = GridletAgentFrameworkService.TryReadFailedCodexToolResult(
            functionResult,
            out var toolName,
            out var result);

        Assert.True(recognized);
        Assert.Equal("describe_table", toolName);
        Assert.Equal("Tool failed.", result);
    }
}
