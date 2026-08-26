using System.Diagnostics;
using System.Security.Cryptography;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gridlet.AspNetCore;

/// <summary>
/// Runs ordinary query streams independently of the request that started them and retains their
/// already-capped event stream briefly so a browser can detach and replay it. Job IDs are
/// capability tokens; authenticated callers must also match the owner recorded at creation.
/// </summary>
internal sealed class GridletQueryJobManager : IAsyncDisposable
{
    private const int EventPageSize = 128;
    private const string UnexpectedErrorMessage = "An unexpected server error occurred.";
    private readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private readonly object jobsGate = new();
    private readonly IOptionsMonitor<GridletOptions> options;
    private readonly IGridletAuditSink audit;
    private readonly ILogger<GridletQueryJobManager> logger;
    private readonly TimeProvider time;
    private bool disposed;

    public GridletQueryJobManager(
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        ILogger<GridletQueryJobManager> logger,
        TimeProvider? time = null)
    {
        this.options = options;
        this.audit = audit;
        this.logger = logger;
        this.time = time ?? TimeProvider.System;
    }

    public QueryJobResponse Start(
        ResolvedConnection resolved,
        string? owner,
        string? userName,
        string sql,
        QueryRequestOptions requestOptions)
    {
        Job job;
        lock (jobsGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            SweepCompletedLocked(time.GetUtcNow());
            var limit = Math.Max(1, options.CurrentValue.Limits.MaxQueryJobs);
            var ownerLimit = Math.Min(
                limit, Math.Max(1, options.CurrentValue.Limits.MaxQueryJobsPerOwner));
            while (jobs.Values.Count(candidate => candidate.IsOwnedBy(owner)) >= ownerLimit)
            {
                var oldestOwnedCompleted = OldestCompleted(owner);
                if (oldestOwnedCompleted is null)
                {
                    throw new GridletValidationException(
                        $"All {ownerLimit} background query jobs are in use for this workspace. " +
                        "Wait for one to finish or cancel it.");
                }
                RemoveLocked(oldestOwnedCompleted);
            }
            while (jobs.Count >= limit)
            {
                var oldestCompleted = jobs.Values
                    .Where(candidate => candidate.IsTerminalSnapshot())
                    .OrderBy(candidate => candidate.CompletedAt)
                    .ThenBy(candidate => candidate.StartedAt)
                    .FirstOrDefault();
                if (oldestCompleted is null)
                {
                    throw new GridletValidationException(
                        $"All {limit} background query jobs are in use. Wait for one to finish or cancel it.");
                }

                RemoveLocked(oldestCompleted);
            }

            string id;
            do
            {
                id = RandomNumberGenerator.GetHexString(32, lowercase: true);
            }
            while (jobs.ContainsKey(id));

            job = new Job(
                id,
                owner,
                userName,
                resolved.Context.ConnectionName,
                resolved.Context.Database ?? throw new GridletValidationException(
                    "A database is required for a background query job."),
                sql,
                token => resolved.Provider.Query.StreamAsync(
                    resolved.Context, sql, requestOptions, parameters: null, cancellationToken: token),
                time.GetUtcNow());
            jobs.Add(id, job);
            // Assignment stays under jobsGate so shutdown cannot snapshot this job before its
            // execution task is visible and then dispose the cancellation source underneath it.
            job.Execution = Task.Run(() => RunAsync(job));
        }

        return Snapshot(job, after: 0, includeEvents: false);
    }

    public async Task<QueryJobResponse?> GetAsync(
        string id,
        string? owner,
        string connection,
        string database,
        int after,
        int waitMilliseconds,
        CancellationToken cancellationToken)
    {
        Job? job;
        lock (jobsGate)
        {
            jobs.TryGetValue(id, out job);
        }
        if (job is null || !job.IsAccessibleBy(owner, connection, database))
        {
            return null;
        }

        Task? changed = null;
        lock (job.Gate)
        {
            ValidateCursor(job, after);
            if (after == job.Events.Count && !job.IsTerminal && waitMilliseconds > 0)
            {
                changed = job.Changed.Task;
            }
            else
            {
                return SnapshotLocked(job, after, includeEvents: true);
            }
        }

        try
        {
            await changed.WaitAsync(
                TimeSpan.FromMilliseconds(Math.Clamp(waitMilliseconds, 1, 2_000)), cancellationToken);
        }
        catch (TimeoutException)
        {
            // A long poll timing out is a normal empty snapshot, not a job failure.
        }

        lock (job.Gate)
        {
            ValidateCursor(job, after);
            return SnapshotLocked(job, after, includeEvents: true);
        }
    }

    public QueryJobResponse? Cancel(
        string id,
        string? owner,
        string connection,
        string database)
    {
        Job? job;
        lock (jobsGate)
        {
            jobs.TryGetValue(id, out job);
        }
        if (job is null || !job.IsAccessibleBy(owner, connection, database))
        {
            return null;
        }

        lock (job.Gate)
        {
            if (!job.IsTerminal && job.Status != "cancelling")
            {
                job.Status = "cancelling";
                job.NotifyLocked();
                job.Cancellation.Cancel();
            }
            return SnapshotLocked(job, after: 0, includeEvents: false);
        }
    }

    public void Sweep()
    {
        lock (jobsGate)
        {
            SweepCompletedLocked(time.GetUtcNow());
        }
    }

    private async Task RunAsync(Job job)
    {
        var stopwatch = Stopwatch.StartNew();
        var sawCompleted = false;
        var sawError = false;
        var finalStatus = "failed";
        string? auditError = null;
        try
        {
            await foreach (var streamEvent in job.StreamFactory(job.Cancellation.Token)
                .WithCancellation(job.Cancellation.Token))
            {
                Publish(job, streamEvent);
                sawCompleted |= string.Equals(streamEvent.Type, "completed", StringComparison.OrdinalIgnoreCase);
                sawError |= string.Equals(streamEvent.Type, "error", StringComparison.OrdinalIgnoreCase);
            }

            if (job.Cancellation.IsCancellationRequested)
            {
                finalStatus = "cancelled";
                auditError = "Cancelled.";
                Publish(job, new QueryStreamEvent("cancelled", DurationMs: stopwatch.ElapsedMilliseconds));
            }
            else if (sawCompleted && !sawError)
            {
                finalStatus = "succeeded";
            }
            else
            {
                auditError = sawError
                    ? "The provider reported a query error."
                    : "The query stream ended before completion.";
                if (!sawError)
                {
                    Publish(job, new QueryStreamEvent(
                        "error", Message: auditError, DurationMs: stopwatch.ElapsedMilliseconds));
                }
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            finalStatus = "cancelled";
            auditError = "Cancelled.";
            Publish(job, new QueryStreamEvent("cancelled", DurationMs: stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            var expected = ex is GridletValidationException or GridletQueryException
                or GridletUnknownConnectionException or GridletObjectNotFoundException;
            var message = expected ? ex.Message : UnexpectedErrorMessage;
            auditError = ex.Message;
            if (!expected)
            {
                logger.LogError(ex, "Background query job {JobId} failed.", job.Id);
            }
            Publish(job, new QueryStreamEvent(
                "error", Message: message, DurationMs: stopwatch.ElapsedMilliseconds));
        }
        finally
        {
            try
            {
                await audit.WriteAsync(new GridletAuditEvent(
                    time.GetUtcNow(), job.UserName, "query.execute", job.ConnectionName,
                    job.Database, null, job.Sql, finalStatus == "succeeded",
                    stopwatch.ElapsedMilliseconds, auditError));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Audit sink failed for background query job {JobId}.", job.Id);
            }

            // Terminal means the entire retained job, including its audit, has completed. Sweep
            // and capacity eviction can then dispose it without abandoning live background work.
            lock (job.Gate)
            {
                job.Status = finalStatus;
                job.CompletedAt = time.GetUtcNow();
                job.NotifyLocked();
            }
        }
    }

    private void Publish(Job job, QueryStreamEvent streamEvent)
    {
        lock (job.Gate)
        {
            var limit = Math.Max(16, options.CurrentValue.Limits.MaxQueryJobEvents);
            var retained = false;
            if (job.Events.Count < limit)
            {
                job.Events.Add(streamEvent);
                retained = true;
            }
            else if (!job.EventsTruncated)
            {
                job.EventsTruncated = true;
                job.Events.Add(new QueryStreamEvent(
                    "message",
                    Message: $"Further query events were omitted after the retained-event limit of {limit}."));
                retained = true;
                retained |= RetainTerminalEvent(job, streamEvent);
            }
            else
            {
                retained = RetainTerminalEvent(job, streamEvent);
            }
            if (retained) job.NotifyLocked();
        }
    }

    private static bool RetainTerminalEvent(Job job, QueryStreamEvent streamEvent)
    {
        if (streamEvent.Type is not ("completed" or "error" or "cancelled")
            || !job.RetainedTerminalEventTypes.Add(streamEvent.Type))
        {
            return false;
        }
        job.Events.Add(streamEvent);
        return true;
    }

    private QueryJobResponse Snapshot(Job job, int after, bool includeEvents)
    {
        lock (job.Gate)
        {
            return SnapshotLocked(job, after, includeEvents);
        }
    }

    private static QueryJobResponse SnapshotLocked(Job job, int after, bool includeEvents)
    {
        var events = includeEvents
            ? job.Events.Skip(after).Take(EventPageSize).ToArray()
            : [];
        return new QueryJobResponse(
            job.Id,
            job.Status,
            job.StartedAt,
            job.CompletedAt,
            after + events.Length,
            job.Events.Count,
            events);
    }

    private static void ValidateCursor(Job job, int after)
    {
        if (after < 0 || after > job.Events.Count)
        {
            throw new GridletValidationException("The query job event cursor is invalid.");
        }
    }

    private void SweepCompletedLocked(DateTimeOffset now)
    {
        var retention = TimeSpan.FromMinutes(
            Math.Max(1, options.CurrentValue.Limits.QueryJobRetentionMinutes));
        foreach (var pair in jobs.Where(pair => pair.Value.ExpiredBy(now, retention)).ToArray())
        {
            RemoveLocked(pair.Value);
        }
    }

    private Job? OldestCompleted(string? owner)
        => jobs.Values
            .Where(candidate => candidate.IsOwnedBy(owner) && candidate.IsTerminalSnapshot())
            .OrderBy(candidate => candidate.CompletedAt)
            .ThenBy(candidate => candidate.StartedAt)
            .FirstOrDefault();

    private void RemoveLocked(Job job)
    {
        jobs.Remove(job.Id);
        job.DisposeCancellation();
    }

    public async ValueTask DisposeAsync()
    {
        Job[] remaining;
        lock (jobsGate)
        {
            if (disposed) return;
            disposed = true;
            remaining = jobs.Values.ToArray();
            jobs.Clear();
        }

        foreach (var job in remaining)
        {
            lock (job.Gate)
            {
                job.Cancellation.Cancel();
            }
        }
        await Task.WhenAll(remaining.Select(job => job.Execution ?? Task.CompletedTask));
        foreach (var job in remaining)
        {
            job.DisposeCancellation();
        }
    }

    private sealed class Job(
        string id,
        string? owner,
        string? userName,
        string connectionName,
        string database,
        string sql,
        Func<CancellationToken, IAsyncEnumerable<QueryStreamEvent>> streamFactory,
        DateTimeOffset startedAt)
    {
        public string Id { get; } = id;
        public string? Owner { get; } = owner;
        public string? UserName { get; } = userName;
        public string ConnectionName { get; } = connectionName;
        public string Database { get; } = database;
        public string Sql { get; } = sql;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public object Gate { get; } = new();
        public List<QueryStreamEvent> Events { get; } = [];
        public CancellationTokenSource Cancellation { get; } = new();
        public Func<CancellationToken, IAsyncEnumerable<QueryStreamEvent>> StreamFactory { get; } = streamFactory;
        public Task? Execution { get; set; }
        public string Status { get; set; } = "running";
        public DateTimeOffset? CompletedAt { get; set; }
        public TaskCompletionSource Changed { get; private set; } = NewSignal();
        public bool EventsTruncated { get; set; }
        public HashSet<string> RetainedTerminalEventTypes { get; } = new(StringComparer.Ordinal);
        public bool IsTerminal => Status is "succeeded" or "failed" or "cancelled";

        public bool IsAccessibleBy(
            string? candidateOwner,
            string candidateConnection,
            string candidateDatabase)
            => string.Equals(Owner, candidateOwner, StringComparison.Ordinal)
                && string.Equals(ConnectionName, candidateConnection, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Database, candidateDatabase, StringComparison.OrdinalIgnoreCase);

        public bool IsOwnedBy(string? candidateOwner)
            => string.Equals(Owner, candidateOwner, StringComparison.Ordinal);

        public bool IsTerminalSnapshot()
        {
            lock (Gate)
            {
                return IsTerminal;
            }
        }

        public bool ExpiredBy(DateTimeOffset now, TimeSpan retention)
        {
            lock (Gate)
            {
                return CompletedAt is { } completed && completed + retention <= now;
            }
        }

        public void NotifyLocked()
        {
            var previous = Changed;
            Changed = NewSignal();
            previous.TrySetResult();
        }

        public void DisposeCancellation()
        {
            lock (Gate)
            {
                Cancellation.Dispose();
            }
        }

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
