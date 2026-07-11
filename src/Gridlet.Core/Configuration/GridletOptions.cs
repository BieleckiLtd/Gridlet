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

    /// <summary>
    /// Adds a database connection to Gridlet's allow-list.
    /// </summary>
    /// <param name="name">Unique display/route name for the connection.</param>
    /// <param name="connectionString">Provider-specific server-side connection string.</param>
    /// <param name="providerName">Registered provider name; defaults to SQL Server.</param>
    /// <param name="configure">Optional per-connection feature-gate configuration.</param>
    /// <returns>This options instance, allowing multiple calls to be chained.</returns>
    public GridletOptions AddConnection(
        string name,
        string connectionString,
        string providerName = GridletProviderNames.SqlServer,
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
}

/// <summary>Well-known provider names.</summary>
public static class GridletProviderNames
{
    /// <summary>Provider name registered by the Gridlet.SqlServer package.</summary>
    public const string SqlServer = "SqlServer";
}
