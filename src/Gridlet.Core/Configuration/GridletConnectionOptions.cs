namespace Gridlet;

/// <summary>A named database connection that Gridlet exposes in its UI and API.</summary>
public sealed class GridletConnectionOptions
{
    /// <summary>Unique display name for the connection (used in routes and the UI).</summary>
    public string Name { get; set; } = "";

    /// <summary>Provider-specific connection string. Never exposed over the API.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Name of the registered <see cref="Abstractions.IGridletProvider"/> that serves this connection.</summary>
    public string ProviderName { get; set; } = GridletProviderNames.SqlServer;

    /// <summary>
    /// Whether the ad-hoc SQL editor may execute statements against this connection.
    /// Statement-level write protection is delegated to the SQL principal's own permissions;
    /// grant the connection's login only the rights users should have.
    /// </summary>
    public bool AllowSqlExecution { get; set; } = true;

    /// <summary>Whether Gridlet's row editing (insert/update/delete rows) is enabled for this connection.</summary>
    public bool AllowWrites { get; set; } = true;

    /// <summary>Whether Gridlet's table designer (create/alter/drop tables and columns) is enabled for this connection.</summary>
    public bool AllowDdl { get; set; } = true;
}
