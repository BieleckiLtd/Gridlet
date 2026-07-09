namespace Gridlet;

/// <summary>
/// Root configuration for Gridlet. Registered via <c>AddGridlet(...)</c> and bound
/// with the standard options pattern, so it can also come from configuration.
/// </summary>
public sealed class GridletOptions
{
    /// <summary>Explicitly configured connections Gridlet is allowed to manage.</summary>
    public IList<GridletConnectionOptions> Connections { get; } = [];

    /// <summary>Safety limits applied to data browsing and query execution.</summary>
    public GridletLimitsOptions Limits { get; set; } = new();

    /// <summary>Authentication/authorization behaviour of the Gridlet endpoints.</summary>
    public GridletSecurityOptions Security { get; set; } = new();

    /// <summary>Adds a named connection.</summary>
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
    public const string SqlServer = "SqlServer";
}
