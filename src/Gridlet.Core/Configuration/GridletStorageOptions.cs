namespace Gridlet;

/// <summary>Where Gridlet persists its own small state (saved queries, published endpoints).</summary>
public sealed class GridletStorageOptions
{
    /// <summary>
    /// Path of the JSON state file. Relative paths resolve against the host's content root.
    /// Replace <see cref="Abstractions.ISavedQueryStore"/> / <see cref="Abstractions.IPublishedEndpointStore"/>
    /// registrations to persist somewhere else entirely.
    /// </summary>
    public string FilePath { get; set; } = "gridlet-store.json";
}
