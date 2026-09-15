using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Reads table/view data in pages.</summary>
public interface ITableDataService
{
    /// <summary>Returns one page of rows from a table or view.</summary>
    Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        TableDataRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns exact aggregate statistics for one validated column.</summary>
    Task<ColumnProfile> GetColumnProfileAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        ColumnProfileRequest request,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support column profiling.");

    /// <summary>Returns the distinct values a column filter offers as a checklist.</summary>
    Task<ColumnFilterValues> GetColumnFilterValuesAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        ColumnFilterValuesRequest request,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not list column filter values.");

    /// <summary>
    /// Returns the provider-specific WHERE clause for the supplied filters followed by the ORDER BY
    /// clause for the sort, with bound values rendered as SQL literals. It describes a data read for
    /// people to see or copy into a query; data reads never run it.
    /// </summary>
    Task<string> GetFilterSqlAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        IReadOnlyList<TableDataFilter>? filters,
        string? sortColumn,
        SortDirection sortDirection,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not describe column filters as SQL.");
}
