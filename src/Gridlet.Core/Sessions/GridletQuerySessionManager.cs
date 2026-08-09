using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Extensions.Options;

namespace Gridlet.Sessions;

/// <summary>
/// Holds the connections of pinned query sessions. Every ordinary execution opens and closes its own
/// connection, which is why an explicit <c>BEGIN TRAN</c> cannot survive it; a session keeps one
/// connection open so a transaction, a temporary table or a <c>SET</c> option lasts until the person
/// ends it.
/// </summary>
/// <remarks>
/// A session is expensive - it holds a database connection and possibly locks - so the number of
/// sessions and their idle lifetime are both capped. The session id is a cryptographically random
/// value and is the only thing that grants access to the session; where the host authenticates
/// requests, the owner is recorded as well and has to match.
/// </remarks>
public sealed class GridletQuerySessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<GridletOptions> options;
    private readonly TimeProvider time;
    private bool disposed;

    public GridletQuerySessionManager(IOptionsMonitor<GridletOptions> options, TimeProvider? time = null)
    {
        this.options = options;
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>The sessions currently open, oldest first.</summary>
    public IReadOnlyList<GridletSessionInfo> List(string? owner)
        => sessions.Values
            .Where(session => session.IsOwnedBy(owner))
            .OrderBy(session => session.OpenedAt)
            .Select(Describe)
            .ToArray();

    /// <summary>
    /// Opens a session on <paramref name="resolved"/>. Throws
    /// <see cref="GridletValidationException"/> when the provider has no session support or the
    /// configured session limit is reached.
    /// </summary>
    public async Task<GridletSessionInfo> OpenAsync(
        ResolvedConnection resolved,
        string? owner,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (resolved.Provider.Query is not IQuerySessionRunner runner)
        {
            throw new GridletValidationException(
                $"Connection '{resolved.Context.ConnectionName}' uses a provider that does not support pinned sessions.");
        }

        await SweepAsync(cancellationToken);
        var limit = Math.Max(1, options.CurrentValue.Limits.MaxQuerySessions);
        if (sessions.Count >= limit)
        {
            throw new GridletValidationException(
                $"All {limit} query sessions are in use. Close one before opening another.");
        }

        var connection = await runner.OpenSessionAsync(resolved.Context, cancellationToken);
        var session = new Session(
            RandomNumberGenerator.GetHexString(32, lowercase: true),
            resolved.Context.ConnectionName,
            resolved.Context.Database,
            owner,
            runner,
            connection,
            time.GetUtcNow());
        if (!sessions.TryAdd(session.Id, session))
        {
            await session.DisposeAsync();
            throw new GridletException("Could not open a query session. Try again.");
        }

        return Describe(session);
    }

    /// <summary>Returns the session's current state, re-reading its transaction status.</summary>
    public async Task<GridletSessionInfo> GetAsync(
        string sessionId,
        string? owner,
        CancellationToken cancellationToken = default)
    {
        var session = Require(sessionId, owner);
        using var lease = await LeaseAsync(session);
        await RefreshTransactionAsync(session, cancellationToken);
        return Describe(session);
    }

    /// <summary>Executes <paramref name="sql"/> on the session's pinned connection.</summary>
    public async IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        string sessionId,
        string? owner,
        string sql,
        QueryRequestOptions requestOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = Require(sessionId, owner);
        using var lease = await LeaseAsync(session);
        try
        {
            await foreach (var streamEvent in session.Runner
                .StreamAsync(session.Connection, sql, requestOptions, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                yield return streamEvent;
            }
        }
        finally
        {
            session.LastUsedAt = time.GetUtcNow();
            // The statement may itself have been BEGIN or COMMIT, or may have been rolled back by
            // the engine, so the recorded state is re-read rather than inferred.
            if (session.Connection.State == ConnectionState.Open)
            {
                await RefreshTransactionAsync(session, CancellationToken.None);
            }

            await AfterUseAsync(session);
        }
    }

    /// <summary>Starts, commits or rolls back the session's transaction.</summary>
    public async Task<GridletSessionInfo> RunTransactionCommandAsync(
        string sessionId,
        string? owner,
        TransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = Require(sessionId, owner);
        using var lease = await LeaseAsync(session);
        try
        {
            session.Transaction = await session.Runner.RunTransactionCommandAsync(
                session.Connection, command, cancellationToken);
            return Describe(session);
        }
        finally
        {
            session.LastUsedAt = time.GetUtcNow();
            await AfterUseAsync(session);
        }
    }

    /// <summary>
    /// Closes a session. Any open transaction is rolled back first, so closing never commits work
    /// nobody asked to commit.
    /// </summary>
    public async Task<bool> CloseAsync(
        string sessionId,
        string? owner,
        CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(sessionId, out var found) || !found.IsOwnedBy(owner))
        {
            return false;
        }

        if (!sessions.TryRemove(new KeyValuePair<string, Session>(sessionId, found)))
        {
            return false;
        }

        await RollbackAndDisposeAsync(found, cancellationToken);
        return true;
    }

    /// <summary>Closes every session that has been idle past the configured timeout.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        foreach (var session in sessions.Values)
        {
            if (ExpiresAt(session) > now)
            {
                continue;
            }

            if (sessions.TryRemove(new KeyValuePair<string, Session>(session.Id, session)))
            {
                await RollbackAndDisposeAsync(session, cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        foreach (var session in sessions.Values)
        {
            if (sessions.TryRemove(new KeyValuePair<string, Session>(session.Id, session)))
            {
                await session.DisposeAsync();
            }
        }
    }

    private Session Require(string sessionId, string? owner)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // An id that belongs to somebody else is reported the same way as one that does not exist,
        // so the response never confirms that a session id is real.
        if (string.IsNullOrEmpty(sessionId)
            || !sessions.TryGetValue(sessionId, out var session)
            || !session.IsOwnedBy(owner))
        {
            throw new GridletSessionNotFoundException(sessionId);
        }

        if (ExpiresAt(session) <= time.GetUtcNow())
        {
            throw new GridletSessionNotFoundException(sessionId);
        }

        return session;
    }

    /// <summary>
    /// Takes the session's single slot. One connection can only run one statement at a time, so a
    /// second request is rejected rather than queued behind a query that may run for minutes.
    /// </summary>
    private static async Task<Lease> LeaseAsync(Session session)
    {
        if (!await session.Gate.WaitAsync(0))
        {
            throw new GridletSessionBusyException(session.Id);
        }

        return new Lease(session);
    }

    private async Task AfterUseAsync(Session session)
    {
        // A connection killed by the server, or dropped by a fatal error, cannot host the rest of
        // the session. Drop it now instead of failing every later request with a closed connection.
        if (session.Connection.State is ConnectionState.Open or ConnectionState.Executing
            or ConnectionState.Fetching)
        {
            return;
        }

        if (sessions.TryRemove(new KeyValuePair<string, Session>(session.Id, session)))
        {
            await session.DisposeAsync();
        }
    }

    private async Task RefreshTransactionAsync(Session session, CancellationToken cancellationToken)
    {
        try
        {
            session.Transaction = await session.Runner.GetTransactionStatusAsync(
                session.Connection, cancellationToken);
        }
        catch (Exception)
        {
            // Reading the state is best effort: it must never replace the error that is already on
            // its way to the caller, nor fail a statement that otherwise succeeded.
            session.Transaction = TransactionStatus.None;
        }
    }

    private async Task RollbackAndDisposeAsync(Session session, CancellationToken cancellationToken)
    {
        try
        {
            if (session.Connection.State == ConnectionState.Open
                && (await session.Runner.GetTransactionStatusAsync(session.Connection, cancellationToken)).IsOpen)
            {
                await session.Runner.RunTransactionCommandAsync(
                    session.Connection, TransactionCommand.Rollback, cancellationToken);
            }
        }
        catch (Exception)
        {
            // Nothing to roll back, or the connection is already gone. Closing it below is enough:
            // an unfinished transaction on a closed connection is rolled back by the engine.
        }

        await session.DisposeAsync();
    }

    private DateTimeOffset ExpiresAt(Session session)
        => session.LastUsedAt.AddMinutes(
            Math.Max(1, options.CurrentValue.Limits.QuerySessionIdleTimeoutMinutes));

    private GridletSessionInfo Describe(Session session)
        => new(
            session.Id,
            session.ConnectionName,
            session.Database,
            session.OpenedAt,
            session.LastUsedAt,
            ExpiresAt(session),
            session.Transaction);

    private sealed class Session(
        string id,
        string connectionName,
        string? database,
        string? owner,
        IQuerySessionRunner runner,
        DbConnection connection,
        DateTimeOffset openedAt) : IAsyncDisposable
    {
        public string Id { get; } = id;

        public string ConnectionName { get; } = connectionName;

        public string? Database { get; } = database;

        public IQuerySessionRunner Runner { get; } = runner;

        public DbConnection Connection { get; } = connection;

        public DateTimeOffset OpenedAt { get; } = openedAt;

        public DateTimeOffset LastUsedAt { get; set; } = openedAt;

        public TransactionStatus Transaction { get; set; } = TransactionStatus.None;

        /// <summary>
        /// The session's single execution slot. It is deliberately never disposed: a session can be
        /// closed while a statement is still running on it, and releasing a disposed semaphore would
        /// throw in the middle of that statement's cleanup.
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool IsOwnedBy(string? candidate)
            => string.Equals(owner, candidate, StringComparison.Ordinal);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.DisposeAsync();
            }
            catch (Exception)
            {
                // The connection is being abandoned; a failure to close it changes nothing.
            }
        }
    }

    private readonly struct Lease(Session session) : IDisposable
    {
        public void Dispose() => session.Gate.Release();
    }
}
