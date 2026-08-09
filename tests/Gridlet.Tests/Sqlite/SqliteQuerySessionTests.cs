using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Sessions;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// A pinned session exists so an explicit transaction survives from one execution to the next.
/// These tests run against a real SQLite file, so the transaction semantics are the engine's.
/// </summary>
public sealed class SqliteQuerySessionTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-session-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private readonly GridletOptions options = new();
    private GridletConnectionContext context = null!;
    private GridletQuerySessionManager sessions = null!;

    private ResolvedConnection Resolved => new(provider, context);

    private static readonly QueryRequestOptions Request = new(100, 30);

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Session",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");
        sessions = new GridletQuerySessionManager(new StaticOptionsMonitor<GridletOptions>(options));

        await provider.Query.ExecuteAsync(context,
            "CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Body TEXT NOT NULL);", Request);
    }

    public async Task DisposeAsync()
    {
        await sessions.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }

    [Fact]
    public async Task A_transaction_started_in_one_execution_is_still_open_in_the_next()
    {
        var session = await sessions.OpenAsync(Resolved, owner: null);
        Assert.False(session.Transaction.IsOpen);

        await RunAsync(session.Id, "BEGIN;");
        await RunAsync(session.Id, "INSERT INTO Notes (Body) VALUES ('pending');");
        var state = await sessions.GetAsync(session.Id, owner: null);

        Assert.True(state.Transaction.IsOpen);
        Assert.Equal(1, state.Transaction.Depth);
        // The row is invisible outside the transaction, which is what "preview a change" means.
        Assert.Equal(0L, await CountNotesOutsideTheSessionAsync());
        Assert.Equal(1L, await ScalarAsync(session.Id, "SELECT COUNT(*) FROM Notes;"));
    }

    [Fact]
    public async Task Committing_from_the_session_makes_the_work_visible_to_everybody()
    {
        var session = await sessions.OpenAsync(Resolved, owner: null);
        await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Begin);
        await RunAsync(session.Id, "INSERT INTO Notes (Body) VALUES ('kept');");

        var committed = await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Commit);

        Assert.False(committed.Transaction.IsOpen);
        Assert.Equal(1L, await CountNotesOutsideTheSessionAsync());
    }

    [Fact]
    public async Task Rolling_back_discards_the_work()
    {
        var session = await sessions.OpenAsync(Resolved, owner: null);
        await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Begin);
        await RunAsync(session.Id, "INSERT INTO Notes (Body) VALUES ('discarded');");

        var rolledBack = await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Rollback);

        Assert.False(rolledBack.Transaction.IsOpen);
        Assert.Equal(0L, await CountNotesOutsideTheSessionAsync());
    }

    [Fact]
    public async Task Closing_a_session_rolls_its_transaction_back_rather_than_committing_it()
    {
        var session = await sessions.OpenAsync(Resolved, owner: null);
        await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Begin);
        await RunAsync(session.Id, "INSERT INTO Notes (Body) VALUES ('abandoned');");

        Assert.True(await sessions.CloseAsync(session.Id, owner: null));

        Assert.Equal(0L, await CountNotesOutsideTheSessionAsync());
        await Assert.ThrowsAsync<GridletSessionNotFoundException>(
            () => sessions.GetAsync(session.Id, owner: null));
    }

    [Fact]
    public async Task A_failed_statement_leaves_the_transaction_for_the_person_to_decide_about()
    {
        var session = await sessions.OpenAsync(Resolved, owner: null);
        await sessions.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Begin);
        await RunAsync(session.Id, "INSERT INTO Notes (Body) VALUES ('first');");

        await Assert.ThrowsAsync<GridletQueryException>(() => RunAsync(session.Id, "SELECT * FROM Nope;"));

        var state = await sessions.GetAsync(session.Id, owner: null);
        Assert.True(state.Transaction.IsOpen);
        Assert.Equal(1L, await ScalarAsync(session.Id, "SELECT COUNT(*) FROM Notes;"));
    }

    [Fact]
    public async Task A_session_belongs_to_the_person_who_opened_it()
    {
        var session = await sessions.OpenAsync(Resolved, owner: "ada");

        await Assert.ThrowsAsync<GridletSessionNotFoundException>(
            () => sessions.GetAsync(session.Id, owner: "grace"));
        await Assert.ThrowsAsync<GridletSessionNotFoundException>(
            () => sessions.GetAsync(session.Id, owner: null));
        Assert.False(await sessions.CloseAsync(session.Id, owner: "grace"));
        Assert.Empty(sessions.List("grace"));
        Assert.Equal(session.Id, Assert.Single(sessions.List("ada")).Id);
    }

    [Fact]
    public async Task Sessions_are_capped_so_they_cannot_exhaust_the_connection_pool()
    {
        options.Limits.MaxQuerySessions = 2;
        await sessions.OpenAsync(Resolved, owner: null);
        await sessions.OpenAsync(Resolved, owner: null);

        var exception = await Assert.ThrowsAsync<GridletValidationException>(
            () => sessions.OpenAsync(Resolved, owner: null));

        Assert.Contains("Close one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_idle_session_is_swept_and_its_transaction_rolled_back()
    {
        options.Limits.QuerySessionIdleTimeoutMinutes = 1;
        var clock = new AdvanceableTimeProvider(DateTimeOffset.UnixEpoch);
        await using var timed = new GridletQuerySessionManager(
            new StaticOptionsMonitor<GridletOptions>(options), clock);
        var session = await timed.OpenAsync(Resolved, owner: null);
        await timed.RunTransactionCommandAsync(session.Id, null, TransactionCommand.Begin);
        await Drain(timed.StreamAsync(session.Id, null, "INSERT INTO Notes (Body) VALUES ('idle');", Request));

        clock.Advance(TimeSpan.FromMinutes(2));
        await timed.SweepAsync();

        await Assert.ThrowsAsync<GridletSessionNotFoundException>(() => timed.GetAsync(session.Id, null));
        Assert.Equal(0L, await CountNotesOutsideTheSessionAsync());
    }

    [Fact]
    public async Task An_unknown_session_is_rejected()
        => await Assert.ThrowsAsync<GridletSessionNotFoundException>(
            () => sessions.GetAsync("not-a-session", owner: null));

    private Task RunAsync(string sessionId, string sql)
        => Drain(sessions.StreamAsync(sessionId, null, sql, Request));

    private static async Task Drain(IAsyncEnumerable<QueryStreamEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private async Task<long> ScalarAsync(string sessionId, string sql)
    {
        object? value = null;
        await foreach (var streamEvent in sessions.StreamAsync(sessionId, null, sql, Request))
        {
            if (streamEvent.Type == "rows") value = streamEvent.Rows![0][0];
        }

        return Convert.ToInt64(value);
    }

    /// <summary>Counts rows on a separate connection, so only committed work is visible.</summary>
    private async Task<long> CountNotesOutsideTheSessionAsync()
    {
        var result = await provider.Query.ExecuteAsync(context, "SELECT COUNT(*) FROM Notes;", Request);
        return Convert.ToInt64(result.ResultSets[0].Rows[0][0]);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class AdvanceableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current = current.Add(by);
    }
}
