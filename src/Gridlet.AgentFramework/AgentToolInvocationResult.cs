namespace Gridlet.AgentFramework;

/// <summary>Provider-neutral representation of a tool invocation reported by a CLI bridge.</summary>
internal sealed record AgentToolInvocationResult(
    string? ToolName,
    bool Success,
    string? Result);
