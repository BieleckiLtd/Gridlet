namespace Gridlet.Models;

/// <summary>Limits applied to a single ad-hoc query execution.</summary>
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
