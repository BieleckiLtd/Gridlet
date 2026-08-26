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
}

/// <summary>
/// One condition on a column. The provider turns it into SQL with the value as a parameter, so a
/// filter can never carry SQL of its own.
/// </summary>
/// <param name="Column">The column to compare. Providers reject a name the object does not have.</param>
/// <param name="Operator">The comparison.</param>
/// <param name="Value">
/// The value to compare against, or <see langword="null"/> for <see cref="FilterOperator.IsNull"/>
/// and <see cref="FilterOperator.IsNotNull"/>.
/// </param>
public sealed record TableDataFilter(string Column, FilterOperator Operator, string? Value = null);

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
public sealed record ResultColumn(string Name, string DataTypeName, bool IsBinary = false);

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
