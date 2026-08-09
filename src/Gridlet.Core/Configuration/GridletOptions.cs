using Microsoft.Extensions.Configuration;

namespace Gridlet;

/// <summary>
/// Root configuration for Gridlet. Registered via <c>AddGridlet(...)</c> and bound
/// with the standard options pattern, so it can also come from configuration.
/// </summary>
public sealed class GridletOptions
{
    /// <summary>
    /// Explicit allow-list of database connections shown by Gridlet. Prefer
    /// <see cref="AddConnection"/> to populate it. Gridlet does not discover arbitrary host
    /// connection strings.
    /// </summary>
    public IList<GridletConnectionOptions> Connections { get; } = [];

    /// <summary>Safety limits applied to data browsing and query execution.</summary>
    public GridletLimitsOptions Limits { get; set; } = new();

    /// <summary>Authentication/authorization behaviour of the Gridlet endpoints.</summary>
    public GridletSecurityOptions Security { get; set; } = new();

    /// <summary>Persistence for saved queries and published endpoints.</summary>
    public GridletStorageOptions Storage { get; set; } = new();

    /// <summary>Privacy controls for the default structured audit logger.</summary>
    public GridletAuditOptions Audit { get; set; } = new();

    /// <summary>
    /// The single route segment, directly under Gridlet's mount, that published API endpoints are
    /// served from. Defaults to <c>pub</c>, so a route named <c>customers</c> under the default
    /// mount answers at <c>/gridlet/pub/customers</c>.
    /// </summary>
    /// <remarks>
    /// Change it to fit a host's own URL conventions. It is one segment rather than a path so that
    /// a published route can never be confused with a deeper part of the mount, and <c>api</c> is
    /// rejected because Gridlet's own management API already occupies it. Existing published
    /// endpoints keep their routes; only the segment in front of them moves, so anything already
    /// calling them has to be updated.
    /// </remarks>
    public string PublishedApiRoutePrefix { get; set; } = "pub";

    /// <summary>
    /// <see cref="PublishedApiRoutePrefix"/> as the bare segment used to build a route or a URL,
    /// with any surrounding slashes removed. A blank value falls back to the default so nothing
    /// builds a URL with an empty segment while validation reports the misconfiguration.
    /// </summary>
    public string PublishedApiSegment =>
        PublishedApiRoutePrefix?.Trim('/') is { Length: > 0 } segment ? segment : "pub";

    /// <summary>
    /// Adds a database connection to Gridlet's allow-list.
    /// </summary>
    /// <param name="name">Unique display/route name for the connection.</param>
    /// <param name="connectionString">Provider-specific server-side connection string.</param>
    /// <param name="providerName">Registered provider serving this connection.</param>
    /// <param name="configure">Optional per-connection feature-gate configuration.</param>
    /// <returns>This options instance, allowing multiple calls to be chained.</returns>
    public GridletOptions AddConnection(
        string name,
        string connectionString,
        GridletProviderNames providerName,
        Action<GridletConnectionOptions>? configure = null)
    {
        var connection = new GridletConnectionOptions
        {
            Name = name,
            ConnectionString = connectionString,
            ProviderName = providerName,
        };
        configure?.Invoke(connection);
        Connections.Add(connection);
        return this;
    }

    /// <summary>
    /// Adds the connection stored under <c>ConnectionStrings:{connectionName}</c>, using that
    /// configuration key as its Gridlet display and route name.
    /// </summary>
    /// <param name="configuration">Host configuration containing the connection string.</param>
    /// <param name="connectionName">Key within the standard <c>ConnectionStrings</c> section.</param>
    /// <param name="providerName">Registered provider serving this connection.</param>
    /// <param name="configure">Optional per-connection feature-gate configuration.</param>
    public GridletOptions AddConnection(
        IConfiguration configuration,
        string connectionName,
        GridletProviderNames providerName,
        Action<GridletConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            throw new GridletValidationException("A connection-string configuration key is required.");
        }

        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new GridletValidationException(
                $"ConnectionStrings:{connectionName} is not configured.");
        return AddConnection(connectionName, connectionString, providerName, configure);
    }
}

/// <summary>Database providers supported by Gridlet.</summary>
public enum GridletProviderNames
{
    /// <summary>No provider selected. This value is rejected by configuration validation.</summary>
    Unspecified,

    /// <summary>Provider registered by the Gridlet.SqlServer package.</summary>
    SqlServer,

    /// <summary>Provider registered by the Gridlet.Sqlite package.</summary>
    Sqlite,
}
