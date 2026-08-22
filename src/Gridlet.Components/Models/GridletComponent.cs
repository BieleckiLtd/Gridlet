using System.Text.Json;

namespace Gridlet.Components;

/// <summary>
/// A component designed in the Gridlet components designer.
/// </summary>
/// <remarks>
/// The stored document is a supported, versioned artifact: readable, diffable in review, and
/// portable between environments by copying it. The envelope below is the part Gridlet interprets;
/// the layout, control tree, custom styles and scripts live in <paramref name="Definition"/>, which
/// the server stores verbatim and never executes. Envelope fields are added, never repurposed, so
/// a document written by an older build keeps loading.
/// </remarks>
/// <param name="SchemaVersion">
/// The version of the definition shape. A document claiming a newer version than this build
/// understands is rejected on save rather than silently downgraded.
/// </param>
/// <param name="Definition">
/// The designer's own document: layout mode, controls, styles and scripts. Opaque to the server by
/// design, because the browser is the only thing that renders a component.
/// </param>
public sealed record GridletComponent(
    string Id,
    string Name,
    int SchemaVersion,
    JsonElement Definition,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>The definition shape this build writes and understands.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Persistence for designed components. The default implementation stores a JSON file under the host's
/// content root; replace the registration to persist elsewhere.
/// </summary>
public interface IComponentStore
{
    Task<IReadOnlyList<GridletComponent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<GridletComponent?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task<GridletComponent> SaveAsync(GridletComponent component, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Host configuration for the components designer.</summary>
public sealed class GridletComponentsOptions
{
    /// <summary>
    /// Where the default store keeps component documents. Relative paths resolve against the host's
    /// content root. Kept separate from Gridlet's own state file so components can be source-controlled
    /// or promoted between environments on their own.
    /// </summary>
    public string FilePath { get; set; } = "gridlet-components.json";

    /// <summary>
    /// Folder holding the JavaScript modules components attach, one file per module. Relative paths
    /// resolve against the host's content root. They are kept as ordinary <c>.js</c> files rather
    /// than inside the component documents so an editor, a linter, a diff and a code review all see
    /// them as the source they are.
    /// </summary>
    public string ScriptsPath { get; set; } = "gridlet-components";
}
