using Gridlet.Auditing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class LoggingGridletAuditSinkTests
{
    [Fact]
    public async Task Compatibility_defaults_include_sql_and_error_details()
    {
        var state = await WriteAuditAsync(new GridletAuditOptions());

        Assert.Equal("SELECT 'private'", state["Sql"]);
        Assert.Equal("sensitive database error", state["Error"]);
    }

    [Fact]
    public async Task Sql_can_be_redacted_without_redacting_error_details()
    {
        var state = await WriteAuditAsync(new GridletAuditOptions
        {
            IncludeSqlText = false,
            IncludeErrorDetails = true,
        });

        Assert.Equal("<redacted>", state["Sql"]);
        Assert.Equal("sensitive database error", state["Error"]);
    }

    [Fact]
    public async Task Error_details_can_be_redacted_without_redacting_sql()
    {
        var state = await WriteAuditAsync(new GridletAuditOptions
        {
            IncludeSqlText = true,
            IncludeErrorDetails = false,
        });

        Assert.Equal("SELECT 'private'", state["Sql"]);
        Assert.Equal("<redacted>", state["Error"]);
    }

    private static async Task<IReadOnlyDictionary<string, object?>> WriteAuditAsync(
        GridletAuditOptions auditOptions)
    {
        var logger = new RecordingLogger();
        var sink = new LoggingGridletAuditSink(
            logger,
            Options.Create(new GridletOptions { Audit = auditOptions }));

        await sink.WriteAsync(new GridletAuditEvent(
            DateTimeOffset.UtcNow,
            "user",
            "query.execute",
            "Main",
            "Database",
            null,
            "SELECT 'private'",
            Succeeded: false,
            DurationMs: 12,
            "sensitive database error"));

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(logger.State);
    }

    private sealed class RecordingLogger : ILogger<LoggingGridletAuditSink>
    {
        public IReadOnlyDictionary<string, object?>? State { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            State = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .ToDictionary(item => item.Key, item => item.Value);
        }
    }
}
