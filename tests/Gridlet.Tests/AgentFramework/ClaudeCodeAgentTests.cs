using System.Text.Json;
using Gridlet.AgentFramework;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class ClaudeCodeAgentTests
{
    [Fact]
    public async Task Mcp_bridge_lists_and_invokes_only_supplied_functions()
    {
        var invoked = false;
        var tool = AIFunctionFactory.Create(
            (string value) =>
            {
                invoked = true;
                return $"read:{value}";
            },
            name: "read_value");
        var updates = new List<AgentResponseUpdate>();

        using var listMessage = JsonDocument.Parse(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");
        var listResponse = await ClaudeCodeRuntime.HandleMcpMessageAsync(
            listMessage.RootElement, [tool], 8, 0, updates, CancellationToken.None);
        var listJson = JsonSerializer.Serialize(listResponse);

        Assert.Contains("read_value", listJson);
        Assert.DoesNotContain("shell", listJson, StringComparison.OrdinalIgnoreCase);

        using var callMessage = JsonDocument.Parse(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"read_value","arguments":{"value":"safe"}}}""");
        var callResponse = await ClaudeCodeRuntime.HandleMcpMessageAsync(
            callMessage.RootElement, [tool], 8, 1, updates, CancellationToken.None);
        var callJson = JsonSerializer.Serialize(callResponse);

        Assert.True(invoked);
        Assert.Contains("read:safe", callJson);
        Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task Mcp_bridge_refuses_calls_above_the_configured_limit()
    {
        var invoked = false;
        var tool = AIFunctionFactory.Create(
            () => invoked = true,
            name: "bounded_tool");
        using var callMessage = JsonDocument.Parse(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"bounded_tool","arguments":{}}}""");

        var response = await ClaudeCodeRuntime.HandleMcpMessageAsync(
            callMessage.RootElement, [tool], 1, 2, [], CancellationToken.None);
        var json = JsonSerializer.Serialize(response);

        Assert.False(invoked);
        Assert.Contains("tool calls was reached", json);
        Assert.Contains("\"isError\":true", json);
    }

    [Fact]
    public void Windows_batch_shims_are_rejected()
    {
        if (!OperatingSystem.IsWindows()) return;

        var exception = Assert.Throws<GridletAgentException>(() =>
            ClaudeCodeRuntime.ResolveExecutablePath("C:\\tools\\claude.cmd"));

        Assert.Contains("native claude.exe", exception.Message);
    }

    [Fact]
    public void Literal_user_slash_commands_are_not_forwarded_to_claude_code()
    {
        Assert.Equal("ordinary question", ClaudeCodeRuntime.EscapeSlashCommand("ordinary question"));

        var escaped = ClaudeCodeRuntime.EscapeSlashCommand("  /clear");

        Assert.DoesNotMatch("^\\s*/", escaped);
        Assert.Contains("literal text", escaped);
        Assert.Contains("/clear", escaped);
    }
}
