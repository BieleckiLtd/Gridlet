using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Extensions.Options;

namespace Gridlet;

/// <summary>Default resolver over <see cref="GridletOptions.Connections"/>.</summary>
public sealed class GridletConnectionResolver(
    IOptionsMonitor<GridletOptions> options,
    IGridletProviderRegistry providerRegistry) : IGridletConnectionResolver
{
    public ResolvedConnection Resolve(string connectionName, string? database = null)
    {
        var connection = options.CurrentValue.Connections.FirstOrDefault(
            c => string.Equals(c.Name, connectionName, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletUnknownConnectionException(connectionName);

        var provider = providerRegistry.Get(connection.ProviderName);
        return new ResolvedConnection(provider, new GridletConnectionContext(connection, database));
    }
}
