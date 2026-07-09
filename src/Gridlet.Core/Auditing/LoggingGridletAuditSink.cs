using Microsoft.Extensions.Logging;

namespace Gridlet.Auditing;

/// <summary>Default audit sink that writes structured entries to the host's logging pipeline.</summary>
public sealed class LoggingGridletAuditSink(ILogger<LoggingGridletAuditSink> logger) : IGridletAuditSink
{
    public ValueTask WriteAsync(GridletAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        logger.Log(
            auditEvent.Succeeded ? LogLevel.Information : LogLevel.Warning,
            "Gridlet audit: {Action} by {User} on {Connection}/{Database} ({ObjectName}) succeeded={Succeeded} duration={DurationMs}ms sql={Sql} error={Error}",
            auditEvent.Action,
            auditEvent.UserName ?? "<anonymous>",
            auditEvent.ConnectionName,
            auditEvent.Database ?? "<default>",
            auditEvent.ObjectName ?? "-",
            auditEvent.Succeeded,
            auditEvent.DurationMs,
            auditEvent.Sql ?? "-",
            auditEvent.Error ?? "-");

        return ValueTask.CompletedTask;
    }
}
