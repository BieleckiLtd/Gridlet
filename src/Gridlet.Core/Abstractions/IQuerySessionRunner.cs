using System.Data.Common;
using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Optional capability of an <see cref="IQueryRunner"/>: run statements on a connection that stays
/// open between executions, so an explicit transaction survives from one execution to the next.
/// Gridlet owns the connection's lifetime and passes it back on every call; the runner must not
/// close it.
/// </summary>
/// <remarks>
/// A runner that does not implement this interface simply has no session support - the connection
/// is opened and closed around each execution, as it always has been.
/// </remarks>
public interface IQuerySessionRunner
{
    /// <summary>Opens a connection Gridlet will keep open until the session is closed.</summary>
    Task<DbConnection> OpenSessionAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Executes <paramref name="sql"/> on the session's connection, yielding events progressively.</summary>
    IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        DbConnection connection,
        string sql,
        QueryRequestOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the connection's current transaction state.</summary>
    Task<TransactionStatus> GetTransactionStatusAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts, commits or rolls back a transaction on the connection and returns the resulting
    /// state. The statement is issued as SQL rather than through
    /// <see cref="DbConnection.BeginTransaction()"/>, so a transaction the person started by typing
    /// <c>BEGIN</c> in the editor and one started from the UI are the same transaction.
    /// </summary>
    Task<TransactionStatus> RunTransactionCommandAsync(
        DbConnection connection,
        TransactionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>A transaction control statement a session can be asked to run.</summary>
public enum TransactionCommand
{
    Begin,
    Commit,
    Rollback,
}
