using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Row-level writes (INSERT/UPDATE/DELETE) against a single table.</summary>
public interface ITableWriteService
{
    /// <summary>Inserts one row. Returns the number of rows affected.</summary>
    Task<int> InsertRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the row identified by <paramref name="key"/>. Returns the number of rows affected.</summary>
    Task<int> UpdateRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the row identified by <paramref name="key"/>. Returns the number of rows affected.</summary>
    Task<int> DeleteRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        CancellationToken cancellationToken = default);
}
