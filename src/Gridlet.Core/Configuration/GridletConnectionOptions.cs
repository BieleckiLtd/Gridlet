namespace Gridlet;

/// <summary>A named database connection that Gridlet exposes in its UI and API.</summary>
public sealed class GridletConnectionOptions
{
    /// <summary>
    /// Unique, case-insensitive display name shown in the connection selector and used in API
    /// routes. It must be non-empty and unique among <see cref="GridletOptions.Connections"/>.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Provider-specific connection string used only by the server. Gridlet never includes it in
    /// metadata or browser responses. Use the least-privileged database identity appropriate for
    /// the enabled features.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Name of the registered <see cref="Abstractions.IGridletProvider"/> that serves this
    /// connection. The matching provider package must also be registered, for example with
    /// <c>AddSqlServer()</c>.
    /// </summary>
    public GridletProviderNames ProviderName { get; set; }

    /// <summary>
    /// Database selected when the UI first opens this connection. Provider-specific registration
    /// derives it from the connection string when available.
    /// </summary>
    public string? DefaultDatabase { get; set; }

    /// <summary>
    /// Whether the ad-hoc SQL editor may execute statements against this connection.
    /// Statement-level write protection is delegated to the SQL principal's own permissions;
    /// grant the connection's login only the rights users should have. Defaults to <c>true</c>.
    /// </summary>
    public bool AllowSqlExecution { get; set; } = true;

    /// <summary>
    /// Whether Gridlet's explicit row editor and insert/update/delete endpoints are enabled for
    /// this connection. Defaults to <c>true</c>. This does not restrict statements entered in the
    /// SQL editor; use database permissions or disable <see cref="AllowSqlExecution"/> for that.
    /// </summary>
    public bool AllowWrites { get; set; } = true;

    /// <summary>
    /// Whether Gridlet's schema and table designer endpoints are enabled for this connection.
    /// Defaults to <c>true</c>. This does not restrict DDL entered in the SQL editor; database
    /// permissions remain authoritative.
    /// </summary>
    public bool AllowDdl { get; set; } = true;

    /// <summary>
    /// Whether an AI agent may inspect schema metadata for this connection. Defaults to
    /// <c>false</c> because metadata can leave the application when a remote model profile is
    /// selected. Design-mode agents can explain and propose schema changes, but never apply them.
    /// </summary>
    public bool AllowAgentSchemaAccess { get; set; } = false;

    /// <summary>
    /// Whether an AI agent may inspect schema metadata and execute bounded, read-only queries for
    /// this connection. Defaults to <c>false</c>. Use a database identity that has only the SELECT
    /// permissions agent users should receive; Gridlet's statement guard is defense in depth, not
    /// a substitute for database permissions.
    /// </summary>
    public bool AllowAgentDataAccess { get; set; } = false;

    /// <summary>
    /// Optional provider-specific connection string used only by the data agent's read-only query
    /// tool. Configure a SELECT-only database identity here when the main Gridlet connection has
    /// broader privileges. It is server-side secret configuration and is never returned by Gridlet.
    /// When <c>null</c>, the main <see cref="ConnectionString"/> is used.
    /// </summary>
    public string? AgentDataConnectionString { get; set; }

    /// <summary>
    /// Explicitly permits the data agent to fall back to the primary Gridlet connection when
    /// <see cref="AgentDataConnectionString"/> is not configured. Defaults to <c>false</c> because
    /// the primary identity commonly has write or DDL privileges.
    /// </summary>
    public bool AllowAgentDataWithPrimaryConnection { get; set; } = false;
}
