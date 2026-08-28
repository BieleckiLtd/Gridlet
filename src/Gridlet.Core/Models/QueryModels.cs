using System.Globalization;
using System.Text.Json.Serialization;

namespace Gridlet.Models;

/// <summary>Limits applied to a single ad-hoc query execution.</summary>
/// <param name="MaxRowsPerResultSet">
/// Maximum rows retained per result set before the provider stops reading and marks the set
/// truncated. A value of <c>0</c> or less disables the cap entirely (unbounded); used by
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
/// <param name="RowIdentity">
/// On a <c>resultSet</c> event for table data, how one row can be addressed for editing.
/// </param>
/// <param name="RowKeys">
/// On a <c>rows</c> event for table data, the identifying values for each row in <paramref name="Rows"/>,
/// ordered by <see cref="RowIdentityInfo.Columns"/>.
/// </param>
public sealed record QueryStreamEvent(
    string Type,
    int? ResultSetIndex = null,
    IReadOnlyList<ResultColumn>? Columns = null,
    IReadOnlyList<object?[]>? Rows = null,
    bool? Truncated = null,
    string? Message = null,
    int? RecordsAffected = null,
    long? DurationMs = null,
    RowIdentityInfo? RowIdentity = null,
    IReadOnlyList<object?[]>? RowKeys = null)
{
    /// <summary>
    /// Exact invariant text for decimal and browser-unsafe integer cells in <see cref="Rows"/>.
    /// Browsers can use it without changing the ordinary JSON-number representation for other API
    /// consumers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string?[]>? ExactValues => ExactNumbers(Rows);

    /// <summary>Exact invariant text for precision-sensitive values in <see cref="RowKeys"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string?[]>? ExactRowKeys => ExactNumbers(RowKeys);

    private static IReadOnlyList<string?[]>? ExactNumbers(IReadOnlyList<object?[]>? rows)
    {
        if (rows is null) return null;
        var result = new string?[rows.Count][];
        var containsExactValue = false;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var values = new string?[row.Length];
            result[rowIndex] = values;
            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                values[columnIndex] = row[columnIndex] switch
                {
                    decimal value => value.ToString(CultureInfo.InvariantCulture),
                    long value when value is > 9_007_199_254_740_991 or < -9_007_199_254_740_991
                        => value.ToString(CultureInfo.InvariantCulture),
                    ulong value when value > 9_007_199_254_740_991
                        => value.ToString(CultureInfo.InvariantCulture),
                    _ => null,
                };
                containsExactValue |= values[columnIndex] is not null;
            }
        }
        return containsExactValue ? result : null;
    }
}
