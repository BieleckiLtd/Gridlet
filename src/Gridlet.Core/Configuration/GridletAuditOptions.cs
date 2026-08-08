namespace Gridlet;

/// <summary>Controls which potentially sensitive details the default audit logger includes.</summary>
public sealed class GridletAuditOptions
{
    /// <summary>
    /// Whether the default logging audit sink includes user-authored SQL text. Defaults to
    /// <c>true</c> for compatibility. Set to <c>false</c> when SQL literals may contain sensitive
    /// data. Custom <see cref="Auditing.IGridletAuditSink"/> implementations are unaffected.
    /// </summary>
    public bool IncludeSqlText { get; set; } = true;

    /// <summary>
    /// Whether the default logging audit sink includes exception and database error details.
    /// Defaults to <c>true</c> for compatibility. Custom
    /// <see cref="Auditing.IGridletAuditSink"/> implementations are unaffected.
    /// </summary>
    public bool IncludeErrorDetails { get; set; } = true;
}
