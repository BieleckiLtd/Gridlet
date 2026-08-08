using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gridlet.Auditing;

/// <summary>Default audit sink that writes structured entries to the host's logging pipeline.</summary>
public sealed class LoggingGridletAuditSink(
    ILogger<LoggingGridletAuditSink> logger,
    IOptions<GridletOptions> options) : IGridletAuditSink
{
    /// <summary>Creates the sink with compatibility defaults that include SQL and error details.</summary>
    public LoggingGridletAuditSink(ILogger<LoggingGridletAuditSink> logger)
        : this(logger, Options.Create(new GridletOptions()))
    {
    }

    public ValueTask WriteAsync(GridletAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var auditOptions = options.Value.Audit;
        var sql = auditOptions.IncludeSqlText
            ? auditEvent.Sql ?? "-"
            : auditEvent.Sql is null ? "-" : "<redacted>";
        var error = auditOptions.IncludeErrorDetails
            ? auditEvent.Error ?? "-"
            : auditEvent.Error is null ? "-" : "<redacted>";

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
            sql,
            error);

        return ValueTask.CompletedTask;
    }
}
