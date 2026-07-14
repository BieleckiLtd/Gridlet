namespace Gridlet;

/// <summary>An agent error whose message is explicitly safe to return to a Gridlet client.</summary>
public sealed class GridletAgentException(string message) : Exception(message);
