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
    /// connection. Defaults to <see cref="GridletProviderNames.SqlServer"/>; the matching provider
    /// package must also be registered, for example with <c>AddSqlServer()</c>.
    /// </summary>
    public string ProviderName { get; set; } = GridletProviderNames.SqlServer;

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
}
