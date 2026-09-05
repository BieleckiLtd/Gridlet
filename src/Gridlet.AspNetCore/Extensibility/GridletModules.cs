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
/// Lets an optional Gridlet package add runtime endpoints beneath the Gridlet mount path.
/// </summary>
/// <remarks>
/// These routes are distinct from the authorized management API. They still receive the
/// authorization applied to the Gridlet route group, but they can serve the artifact that an
/// end-user is meant to open rather than the JSON used by the designer.
/// </remarks>
public interface IGridletRuntimeContributor
{
    /// <summary>Maps the package's end-user runtime routes into the Gridlet route group.</summary>
    /// <param name="gridlet">The <c>{mount}</c> route group.</param>
    void Map(IEndpointRouteBuilder gridlet);
}

/// <summary>
/// Optional extension for a runtime package that can be mounted at an application-root path
/// instead of beneath the Gridlet management mount. This keeps public URLs independent from
/// <c>/gridlet/api</c> while retaining the same authorization boundary.
/// </summary>
public interface IGridletRootRuntimeContributor
{
    /// <summary>
    /// Application-root path at which the contributor wants to be mounted, or <see langword="null"/>
    /// when it uses the ordinary Gridlet mount.
    /// </summary>
    string? RootPath { get; }

    /// <summary>Maps the contributor's routes into an already-authorized root route group.</summary>
    void MapAtRoot(IEndpointRouteBuilder root);
}

/// <summary>Non-secret route metadata a runtime package exposes to the Gridlet designer.</summary>
public interface IGridletRuntimeRouteMetadata
{
    /// <summary>Configured component-public path, relative or application-root.</summary>
    string ComponentPublicPath { get; }
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
