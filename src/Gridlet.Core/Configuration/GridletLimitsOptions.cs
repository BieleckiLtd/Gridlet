namespace Gridlet;

/// <summary>Safety limits applied to data browsing and query execution.</summary>
public sealed class GridletLimitsOptions
{
    /// <summary>
    /// Default page size for consumers of the paged table-data API. The interactive UI uses
    /// progressive streaming, but the paged endpoint remains available and is clamped to
    /// <see cref="MaxPageSize"/>.
    /// Defaults to <c>50</c> and must be at least <c>1</c>.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Server-enforced maximum page size for the paged table-data API and maximum batch size used
    /// by progressive table/view streaming. Defaults to <c>500</c>
    /// and must be greater than or equal to <see cref="DefaultPageSize"/>.
    /// </summary>
    public int MaxPageSize { get; set; } = 500;

    /// <summary>
    /// Server-enforced maximum number of rows retained for each ad-hoc query result set or
    /// progressively streamed table/view.
    /// The query editor exposes a per-browser row-cap control, but its value is clamped to this
    /// maximum on the server. Results stream progressively and the UI virtualizes after 1,000
    /// rows; this limit still protects server and browser memory. Defaults to <c>10,000</c>.
    /// </summary>
    public int MaxQueryResultRows { get; set; } = 10_000;

    /// <summary>
    /// Database command timeout in seconds. Query execution is cancelled by the provider when
    /// this duration is exceeded; the UI Cancel button can cancel it earlier. Defaults to
    /// <c>30</c> and must be at least <c>1</c>.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
