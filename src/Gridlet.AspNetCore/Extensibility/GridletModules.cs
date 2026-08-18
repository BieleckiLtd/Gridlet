using Microsoft.AspNetCore.Routing;

namespace Gridlet.AspNetCore.Extensibility;

/// <summary>
/// Lets an optional Gridlet package add endpoints beneath the Gridlet mount path.
/// </summary>
/// <remarks>
/// Contributors are mapped into the same <c>/api</c> group as the built-in endpoints, so they
/// inherit Gridlet's authorization without restating it. A package that is not installed
/// contributes nothing, which is what makes a module genuinely opt-in.
/// </remarks>
public interface IGridletEndpointContributor
{
    /// <summary>Maps the module's endpoints into Gridlet's authorized API group.</summary>
    /// <param name="api">The <c>{mount}/api</c> route group.</param>
    void Map(IEndpointRouteBuilder api);
}

/// <summary>
/// Lets an optional Gridlet package ship its own browser assets and have the shell load them.
/// </summary>
/// <remarks>
/// Assets are served from <c>{mount}/assets/modules/{name}/…</c> and are announced to the browser
/// through <c>api/meta</c>, so the base UI bundle carries no code for modules the host did not
/// install. A module's scripts run in the shell's page and extend it through <c>window.gridlet</c>.
/// </remarks>
public interface IGridletUiAssetProvider
{
    /// <summary>
    /// Module name, used as the asset route segment and as the module's identity in <c>api/meta</c>.
    /// Lowercase, no slashes.
    /// </summary>
    string Name { get; }

    /// <summary>Scripts the shell loads, in order, relative to the module's asset root.</summary>
    IReadOnlyList<string> Scripts { get; }

    /// <summary>Stylesheets the shell loads, relative to the module's asset root.</summary>
    IReadOnlyList<string> Styles { get; }

    /// <summary>
    /// Opens an asset, or returns <c>null</c> when the module does not have one at that path.
    /// Implementations must reject any path that escapes the module's own asset root.
    /// </summary>
    Stream? Open(string relativePath);
}
