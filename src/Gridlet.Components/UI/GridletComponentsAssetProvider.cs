using System.Reflection;
using Gridlet.AspNetCore.Extensibility;

namespace Gridlet.Components.UI;

/// <summary>
/// Serves the designer's browser assets from this assembly's embedded resources.
/// </summary>
/// <remarks>
/// Only the assets this module declares can be opened. That is a deliberate allow-list rather than
/// a path check: a request can never name a resource the module did not intend to publish, so
/// there is no traversal to get wrong.
/// </remarks>
internal sealed class GridletComponentsAssetProvider : IGridletUiAssetProvider
{
    private const string ResourcePrefix = "Gridlet.Components.UI.wwwroot.";

    public string Name => "components";

    // format.js first: it defines the component document format, and the designer reads and writes
    // documents through it from the moment it loads.
    public IReadOnlyList<string> Scripts { get; } = ["format.js", "designer.js"];

    public IReadOnlyList<string> Styles { get; } = ["designer.css"];

    public Stream? Open(string relativePath)
    {
        var known = Scripts.Concat(Styles)
            .FirstOrDefault(a => string.Equals(a, relativePath, StringComparison.Ordinal));
        return known is null
            ? null
            : typeof(GridletComponentsAssetProvider).Assembly
                .GetManifestResourceStream(ResourcePrefix + known);
    }
}
