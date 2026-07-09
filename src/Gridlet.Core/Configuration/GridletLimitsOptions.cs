namespace Gridlet;

/// <summary>Safety limits applied to data browsing and query execution.</summary>
public sealed class GridletLimitsOptions
{
    /// <summary>Page size used by the data grid when the client does not specify one.</summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>Upper bound for any requested page size.</summary>
    public int MaxPageSize { get; set; } = 500;

    /// <summary>Maximum number of rows returned per result set from the ad-hoc query editor.</summary>
    public int MaxQueryResultRows { get; set; } = 5000;

    /// <summary>Command timeout, in seconds, for all database commands issued by Gridlet.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
