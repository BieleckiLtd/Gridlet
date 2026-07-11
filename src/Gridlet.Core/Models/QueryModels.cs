namespace Gridlet.Models;

/// <summary>Limits applied to a single ad-hoc query execution.</summary>
/// <param name="MaxRowsPerResultSet">
/// Maximum rows retained per result set before the provider stops reading and marks the set
/// truncated. A value of <c>0</c> or less disables the cap entirely (unbounded) — used by
/// published endpoints that opt out of the row limit and paginate in SQL themselves.
/// </param>
/// <param name="CommandTimeoutSeconds">Database command timeout in seconds.</param>
public sealed record QueryRequestOptions(int MaxRowsPerResultSet, int CommandTimeoutSeconds);

/// <summary>One result set produced by an ad-hoc query.</summary>
public sealed record QueryResultSet(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated);

/// <summary>The outcome of an ad-hoc query execution.</summary>
public sealed record QueryResult(
    IReadOnlyList<QueryResultSet> ResultSets,
    int RecordsAffected,
    IReadOnlyList<string> Messages,
    long DurationMs);

/// <summary>A progressive event emitted while an interactive query is executing.</summary>
public sealed record QueryStreamEvent(
    string Type,
    int? ResultSetIndex = null,
    IReadOnlyList<ResultColumn>? Columns = null,
    IReadOnlyList<object?[]>? Rows = null,
    bool? Truncated = null,
    string? Message = null,
    int? RecordsAffected = null,
    long? DurationMs = null);
