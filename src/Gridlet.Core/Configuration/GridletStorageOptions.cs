namespace Gridlet;

/// <summary>Where Gridlet persists its own small state (saved queries, published endpoints).</summary>
public sealed class GridletStorageOptions
{
    /// <summary>
    /// Path of the JSON state file. Relative paths resolve against the host's content root.
    /// The hosting process must have read/write access to the containing directory. This file
    /// stores Gridlet metadata, not query result data or database connection strings.
    /// Replace <see cref="Abstractions.ISavedQueryStore"/> / <see cref="Abstractions.IPublishedEndpointStore"/>
    /// registrations to persist somewhere else entirely.
    /// </summary>
    public string FilePath { get; set; } = "gridlet-store.json";
}
