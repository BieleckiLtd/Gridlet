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

    /// <summary>The standalone component viewer uses this without loading it into the designer shell.</summary>
    public string RuntimeScript => "runtime.js";

    // component.css first: it declares the layer order everything else is written against, and a
    // layer named for the first time later would be added at the end of that order rather than in
    // its place. It is also the one stylesheet the standalone runtime links, so a rule that paints
    // a component is written once and cannot say two different things in two places.
    public IReadOnlyList<string> Styles { get; } = ["component.css", "designer.css"];

    /// <summary>The stylesheet that paints a component, shared by the designer and the viewer.</summary>
    public string ComponentStylesheet => "component.css";

    public Stream? Open(string relativePath)
    {
        var known = Scripts.Concat(Styles).Append(RuntimeScript)
            .FirstOrDefault(a => string.Equals(a, relativePath, StringComparison.Ordinal));
        return known is null
            ? null
            : typeof(GridletComponentsAssetProvider).Assembly
                .GetManifestResourceStream(ResourcePrefix + known);
    }
}
