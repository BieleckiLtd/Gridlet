using System.Text.Json.Serialization;

namespace Gridlet.Models;

/// <summary>One key and its configured human-readable label.</summary>
public sealed record ForeignKeyLookupItem(object? Key, object? Label);

public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>How a filter compares a column against a value.</summary>
public enum FilterOperator
{
    Equals,
    NotEquals,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    IsNull,
    IsNotNull,

    /// <summary>The column's text does not start with the value.</summary>
    NotStartsWith,

    /// <summary>The column's text does not end with the value.</summary>
    NotEndsWith,

    /// <summary>
    /// The column's text matches a spreadsheet pattern: <c>*</c> stands for any run of characters,
    /// <c>?</c> for one character, and <c>~</c> makes the character after it literal.
    /// </summary>
    Matches,

    /// <summary>The column's text does not match the <see cref="Matches"/> pattern.</summary>
    NotMatches,

    /// <summary>The column equals one of <see cref="TableDataFilter.Values"/>.</summary>
    In,

    /// <summary>The column equals none of <see cref="TableDataFilter.Values"/>.</summary>
    NotIn,

    /// <summary>The column is null or, for a text column, empty.</summary>
    IsBlank,

    /// <summary>The column has a value, and for a text column a non-empty one.</summary>
    IsNotBlank,

    /// <summary>
    /// The column is at least the value-th largest value of the whole object, so ties are kept.
    /// Other filters do not narrow the ranking, which is how a spreadsheet's Top 10 behaves.
    /// </summary>
    Top,

    /// <summary>The column is at most the value-th smallest value of the whole object.</summary>
    Bottom,

    /// <summary>The column is within the largest value percent of the object's values.</summary>
    TopPercent,

    /// <summary>The column is within the smallest value percent of the object's values.</summary>
    BottomPercent,

    /// <summary>The column is above the average of the whole object.</summary>
    AboveAverage,

    /// <summary>The column is below the average of the whole object.</summary>
    BelowAverage,

    /// <summary>The date's month, 1 to 12, equals the value, in any year.</summary>
    MonthEquals,

    /// <summary>The date's quarter, 1 to 4, equals the value, in any year.</summary>
    QuarterEquals,

    /// <summary>At least one of <see cref="TableDataFilter.Conditions"/> holds.</summary>
    AnyOf,

    /// <summary>Every one of <see cref="TableDataFilter.Conditions"/> holds.</summary>
    AllOf,
}

/// <summary>
/// One condition on a column. The provider turns it into SQL with the value as a parameter, so a
/// filter can never carry SQL of its own.
/// </summary>
/// <param name="Column">
/// The column to compare. Providers reject a name the object does not have. A group ignores it and
/// uses the columns of its own conditions.
/// </param>
/// <param name="Operator">The comparison.</param>
/// <param name="Value">
/// The value to compare against, or <see langword="null"/> for the operators that need none: the
/// null and blank checks, the averages, the lists and the groups.
/// </param>
public sealed record TableDataFilter(string Column, FilterOperator Operator, string? Value = null)
{
    /// <summary>The values <see cref="FilterOperator.In"/> and <see cref="FilterOperator.NotIn"/> compare against.</summary>
    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>The conditions <see cref="FilterOperator.AnyOf"/> and <see cref="FilterOperator.AllOf"/> combine.</summary>
    public IReadOnlyList<TableDataFilter>? Conditions { get; init; }
}

/// <summary>Requests the distinct values a column filter offers as a checklist.</summary>
/// <param name="Column">The column whose values are listed.</param>
/// <param name="Filters">
/// Conditions on the other columns, so the list offers only the values of rows those leave visible.
/// </param>
/// <param name="Search">When set, only values whose text contains it are listed.</param>
/// <param name="Limit">The most values to return.</param>
public sealed record ColumnFilterValuesRequest(
    string Column,
    IReadOnlyList<TableDataFilter>? Filters = null,
    string? Search = null,
    int Limit = 10_000);

/// <summary>The distinct values a column filter offers as a checklist.</summary>
/// <param name="Values">The distinct values that are neither null nor empty text, in ascending order.</param>
/// <param name="HasBlanks">Whether a row in scope has a null or empty value.</param>
/// <param name="IsTruncated">Whether more values exist than <see cref="ColumnFilterValuesRequest.Limit"/> allowed.</param>
public sealed record ColumnFilterValues(IReadOnlyList<object?> Values, bool HasBlanks, bool IsTruncated);

/// <summary>One value and its exact frequency within a column profile.</summary>
public sealed record ColumnProfileValue(
    object? Value,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    long Count);

/// <summary>Requests exact aggregate statistics for one table or view column.</summary>
public sealed record ColumnProfileRequest(
    string Column,
    int TopValues = 10,
    IReadOnlyList<TableDataFilter>? Filters = null);

/// <summary>Exact aggregate statistics for one column over the requested row scope.</summary>
/// <param name="DistinctCount">
/// The number of distinct non-null values, or <see langword="null"/> when the database type cannot
/// participate in equality/grouping operations.
/// </param>
/// <param name="Limitation">
/// Explains which aggregates the database type could not provide. Counts that are present remain
/// exact even when another aggregate is unavailable.
/// </param>
public sealed record ColumnProfile(
    string Column,
    string DataType,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    long TotalCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    long NullCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    long? DistinctCount,
    object? Minimum,
    object? Maximum,
    IReadOnlyList<ColumnProfileValue> TopValues,
    string? Limitation = null);

/// <summary>A request for one page of table/view data.</summary>
/// <param name="Filters">
/// Conditions every returned row must satisfy, combined with AND. They also apply to the reported
/// total, so paging stays consistent with what is on screen.
/// </param>
public sealed record TableDataRequest(
    int Page,
    int PageSize,
    string? SortColumn = null,
    SortDirection SortDirection = SortDirection.Ascending,
    IReadOnlyList<TableDataFilter>? Filters = null)
{
    /// <summary>Creates the legacy four-field request shape without relying on optional-parameter ABI.</summary>
    public TableDataRequest(
        int page,
        int pageSize,
        string? sortColumn,
        SortDirection sortDirection)
        : this(page, pageSize, sortColumn, sortDirection, null)
    {
    }
}

/// <summary>A column of a result set, with the provider's type name for display.</summary>
public sealed record ResultColumn(string Name, string DataTypeName);

/// <summary>One page of table/view data.</summary>
/// <param name="RowIdentity">
/// How one row of this page can be addressed for editing, or <see langword="null"/> when the
/// provider cannot identify a single row.
/// </param>
/// <param name="RowKeys">
/// One entry per row in <paramref name="Rows"/>, holding that row's identifying values in the order
/// given by <see cref="RowIdentityInfo.Columns"/>. Populated whenever
/// <paramref name="RowIdentity"/> is present, including when the identifying values are also
/// visible in <paramref name="Columns"/>, so callers never have to reconstruct the key themselves.
/// </param>
public sealed record TableDataPage(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int Page,
    int PageSize,
    long TotalRows,
    RowIdentityInfo? RowIdentity = null,
    IReadOnlyList<object?[]>? RowKeys = null)
{
    /// <summary>Creates the legacy five-field page shape without relying on optional-parameter ABI.</summary>
    public TableDataPage(
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<object?[]> rows,
        int page,
        int pageSize,
        long totalRows)
        : this(columns, rows, page, pageSize, totalRows, null, null)
    {
    }
}

/// <summary>A rectangular batch of values to append to one table atomically.</summary>
public sealed record TableImport(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>The outcome of a completed table import.</summary>
public sealed record TableImportResult(int RowsImported);
