using System.Diagnostics.CodeAnalysis;

namespace Gridlet.Abstractions;

/// <summary>Resolves registered <see cref="IGridletProvider"/> implementations by name.</summary>
public interface IGridletProviderRegistry
{
    /// <summary>All registered providers.</summary>
    IReadOnlyList<IGridletProvider> All { get; }

    /// <summary>Returns the provider with the given name, or throws <see cref="GridletUnknownProviderException"/>.</summary>
    IGridletProvider Get(string providerName);

    bool TryGet(string providerName, [NotNullWhen(true)] out IGridletProvider? provider);
}
