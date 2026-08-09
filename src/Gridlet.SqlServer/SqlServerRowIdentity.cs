using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>
/// Chooses how one row of a SQL Server table can be addressed for editing. A declared primary key is
/// preferred; a heap or a table with only unique indexes falls back to the narrowest unique key whose
/// columns are all non-nullable, which is the only kind of unique key that identifies exactly one row.
/// </summary>
internal static class SqlServerRowIdentity
{
    /// <summary>A unique constraint or unique index considered as a row identity.</summary>
    /// <param name="Name">The constraint or index name.</param>
    /// <param name="Columns">The key columns, in key order.</param>
    /// <param name="IsDisabled">Whether the backing index is disabled.</param>
    /// <param name="IsFiltered">Whether the index has a filter, so it covers only some rows.</param>
    internal readonly record struct UniqueKey(
        string Name,
        IReadOnlyList<string> Columns,
        bool IsDisabled = false,
        bool IsFiltered = false);

    /// <summary>
    /// Returns the row identity, or <see langword="null"/> when no key addresses exactly one row.
    /// </summary>
    /// <param name="primaryKeyColumns">The primary-key columns in key order, empty when there is none.</param>
    /// <param name="uniqueKeys">Unique constraints and unique indexes on the table.</param>
    /// <param name="nullableColumns">
    /// Every column of the table mapped to its nullability. A unique key over a nullable column is
    /// rejected: SQL Server allows one NULL per key there, but a NULL comparison in the row's WHERE
    /// clause would not match it back.
    /// </param>
    internal static RowIdentityInfo? Resolve(
        IReadOnlyList<string> primaryKeyColumns,
        IEnumerable<UniqueKey> uniqueKeys,
        IReadOnlyDictionary<string, bool> nullableColumns)
    {
        if (primaryKeyColumns.Count > 0)
        {
            return new RowIdentityInfo(RowIdentityKinds.PrimaryKey, [.. primaryKeyColumns]);
        }

        var candidate = uniqueKeys
            .Where(key => key.Columns.Count > 0
                && !key.IsDisabled
                && !key.IsFiltered
                && key.Columns.All(column =>
                    nullableColumns.TryGetValue(column, out var isNullable) && !isNullable))
            .OrderBy(key => key.Columns.Count)
            .ThenBy(key => key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(key => (UniqueKey?)key)
            .FirstOrDefault();

        return candidate is null
            ? null
            : new RowIdentityInfo(RowIdentityKinds.UniqueKey, [.. candidate.Value.Columns], candidate.Value.Name);
    }
}
