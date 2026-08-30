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
}
