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
public sealed record TableDataPage(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int Page,
    int PageSize,
    long TotalRows);
