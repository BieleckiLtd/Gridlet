using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Executes ad-hoc SQL authored by the user, subject to the configured limits.</summary>
public interface IQueryRunner
{
    /// <summary>
    /// Executes <paramref name="sql"/> and returns all result sets (each capped at
    /// <see cref="QueryRequestOptions.MaxRowsPerResultSet"/> rows), informational messages,
    /// and the affected-record count. <paramref name="parameters"/> are passed as SQL
    /// parameters (used by published API endpoints). Throws <see cref="GridletQueryException"/>
    /// when the database rejects the statement.
    /// </summary>
    Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);
}
