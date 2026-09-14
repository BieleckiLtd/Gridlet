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
    /// Returns the provider-specific WHERE clause used for the supplied filters, with its bound
    /// values rendered as SQL literals for display only.
    /// </summary>
    Task<string> GetFilterSqlAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        IReadOnlyList<TableDataFilter>? filters,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not describe column filters as SQL.");
}
