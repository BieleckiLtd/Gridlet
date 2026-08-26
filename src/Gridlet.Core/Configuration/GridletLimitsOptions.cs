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

    /// <summary>
    /// How many pinned query sessions may be open at once across this Gridlet. Each one holds a
    /// database connection, and an open transaction on it can hold locks, so the count is capped.
    /// Defaults to <c>4</c> and must be at least <c>1</c>.
    /// </summary>
    public int MaxQuerySessions { get; set; } = 4;

    /// <summary>
    /// How long a pinned query session may sit idle before Gridlet rolls back its transaction and
    /// closes it. Defaults to <c>15</c> minutes and must be at least <c>1</c>.
    /// </summary>
    public int QuerySessionIdleTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum ordinary (non-session) query jobs retained in memory, including running jobs and
    /// completed results awaiting reattachment. The oldest completed result is evicted first when
    /// capacity is needed. Defaults to <c>8</c> and must be at least <c>1</c>.
    /// </summary>
    public int MaxQueryJobs { get; set; } = 8;

    /// <summary>
    /// Maximum query jobs one authenticated owner (or the shared anonymous workspace) may retain.
    /// The effective value never exceeds <see cref="MaxQueryJobs"/>. Defaults to <c>4</c> and must
    /// be at least <c>1</c>.
    /// </summary>
    public int MaxQueryJobsPerOwner { get; set; } = 4;

    /// <summary>
    /// Maximum ordinary stream events retained per job before further events are omitted with a
    /// marker. Terminal state is always retained. Defaults to <c>2048</c> and must be at least
    /// <c>16</c>.
    /// </summary>
    public int MaxQueryJobEvents { get; set; } = 2_048;

    /// <summary>
    /// Maximum serialized event payload retained by one query job. This bounds wide rows and
    /// multi-result scripts independently of the event-count limit. Defaults to <c>16 MiB</c> and
    /// must be at least <c>64 KiB</c>.
    /// </summary>
    public int MaxQueryJobRetainedBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// How long a finished query job and its capped results remain available for reattachment.
    /// Defaults to <c>15</c> minutes and must be at least <c>1</c>.
    /// </summary>
    public int QueryJobRetentionMinutes { get; set; } = 15;
}
