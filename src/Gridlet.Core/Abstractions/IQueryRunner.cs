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

    /// <summary>
    /// Executes a query and yields metadata and rows progressively. Providers may rely on this
    /// buffered fallback; providers that support streaming should override it.
    /// </summary>
    async IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(context, sql, options, parameters, cancellationToken);
        yield return new QueryStreamEvent("started");
        for (var index = 0; index < result.ResultSets.Count; index++)
        {
            var set = result.ResultSets[index];
            yield return new QueryStreamEvent("resultSet", index, set.Columns);
            if (set.Rows.Count > 0)
            {
                yield return new QueryStreamEvent("rows", index, Rows: set.Rows);
            }
            yield return new QueryStreamEvent("resultSetCompleted", index, Truncated: set.Truncated);
        }
        foreach (var message in result.Messages)
        {
            yield return new QueryStreamEvent("message", Message: message);
        }
        yield return new QueryStreamEvent("completed", RecordsAffected: result.RecordsAffected, DurationMs: result.DurationMs);
    }
}
