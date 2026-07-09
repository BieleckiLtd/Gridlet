using System.Diagnostics.CodeAnalysis;
using Gridlet.Abstractions;

namespace Gridlet;

/// <summary>Default registry backed by the providers registered in dependency injection.</summary>
public sealed class GridletProviderRegistry : IGridletProviderRegistry
{
    private readonly Dictionary<string, IGridletProvider> _providers;

    public GridletProviderRegistry(IEnumerable<IGridletProvider> providers)
    {
        _providers = new Dictionary<string, IGridletProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            _providers[provider.ProviderName] = provider;
        }

        All = _providers.Values.ToArray();
    }

    public IReadOnlyList<IGridletProvider> All { get; }

    public IGridletProvider Get(string providerName)
        => TryGet(providerName, out var provider)
            ? provider
            : throw new GridletUnknownProviderException(providerName);

    public bool TryGet(string providerName, [NotNullWhen(true)] out IGridletProvider? provider)
        => _providers.TryGetValue(providerName, out provider);
}
