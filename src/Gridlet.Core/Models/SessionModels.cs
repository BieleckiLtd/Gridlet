namespace Gridlet.Models;

/// <summary>The transaction state of a pinned session's connection.</summary>
/// <param name="IsOpen">Whether an explicit transaction is currently open.</param>
/// <param name="Depth">
/// How many transactions are nested. SQL Server reports <c>@@TRANCOUNT</c>; SQLite has a single
/// level, so the depth is 0 or 1.
/// </param>
/// <param name="IsUncommittable">
/// Whether the transaction can no longer be committed and only a rollback will end it (SQL Server's
/// doomed transaction, <c>XACT_STATE() = -1</c>).
/// </param>
public sealed record TransactionStatus(bool IsOpen, int Depth, bool IsUncommittable = false)
{
    /// <summary>No explicit transaction: the connection is in autocommit.</summary>
    public static TransactionStatus None { get; } = new(false, 0);
}

/// <summary>A database connection held open across executions for one interactive session.</summary>
/// <param name="Id">The opaque session identifier. It is the only thing that grants access to the session.</param>
/// <param name="ConnectionName">The Gridlet connection the session runs on.</param>
/// <param name="Database">The database the session is pinned to.</param>
/// <param name="OpenedAt">When the session was opened.</param>
/// <param name="LastUsedAt">When the session last ran a statement.</param>
/// <param name="ExpiresAt">When the session will be closed if it stays idle.</param>
/// <param name="Transaction">The transaction state as of the last observation.</param>
public sealed record GridletSessionInfo(
    string Id,
    string ConnectionName,
    string? Database,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    TransactionStatus Transaction);
