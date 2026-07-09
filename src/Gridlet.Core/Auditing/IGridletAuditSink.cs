namespace Gridlet.Auditing;

/// <summary>A single auditable action performed through Gridlet.</summary>
public sealed record GridletAuditEvent(
    DateTimeOffset Timestamp,
    string? UserName,
    string Action,
    string ConnectionName,
    string? Database,
    string? ObjectName,
    string? Sql,
    bool Succeeded,
    long DurationMs,
    string? Error);

/// <summary>
/// Receives audit events for actions performed through Gridlet. Replace the default
/// (log-based) sink to persist audit events to your own store.
/// </summary>
public interface IGridletAuditSink
{
    ValueTask WriteAsync(GridletAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
