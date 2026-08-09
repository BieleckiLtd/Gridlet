namespace Gridlet.Models;

public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>A request for one page of table/view data.</summary>
public sealed record TableDataRequest(
    int Page,
    int PageSize,
    string? SortColumn = null,
    SortDirection SortDirection = SortDirection.Ascending);

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
