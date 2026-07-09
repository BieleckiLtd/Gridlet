using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>A configured connection resolved to its provider, ready to execute operations.</summary>
public sealed record ResolvedConnection(IGridletProvider Provider, GridletConnectionContext Context);

/// <summary>Resolves a connection name (plus optional target database) into a provider and context.</summary>
public interface IGridletConnectionResolver
{
    /// <summary>
    /// Throws <see cref="GridletUnknownConnectionException"/> for unknown names and
    /// <see cref="GridletUnknownProviderException"/> when the connection's provider is not registered.
    /// </summary>
    ResolvedConnection Resolve(string connectionName, string? database = null);
}
