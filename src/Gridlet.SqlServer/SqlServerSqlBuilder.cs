using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>Builds the dynamic SQL Gridlet needs. Identifiers are always bracket-quoted; values are always parameters.</summary>
public static class SqlServerSqlBuilder
{
    /// <summary>
    /// Builds a paged <c>SELECT</c> over a table or view. Expects <c>@Offset</c> and
    /// <c>@PageSize</c> parameters. Without a sort column the row order is engine-defined.
    /// </summary>
    public static string BuildPageSql(string schema, string name, string? sortColumn, SortDirection sortDirection)
        => BuildPageSql(schema, name, sortColumn, sortDirection, primaryKeyColumns: null);

    /// <summary>
    /// Builds a paged <c>SELECT</c>, appending primary-key columns as deterministic tie-breakers.
    /// When no explicit sort is supplied, the primary key provides the complete ordering. If no
    /// usable primary key is available, the engine-defined legacy ordering is retained.
    /// </summary>
    public static string BuildPageSql(
        string schema,
        string name,
        string? sortColumn,
        SortDirection sortDirection,
        IReadOnlyList<string>? primaryKeyColumns)
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, name);
        var orderByColumns = new List<string>();
        if (sortColumn is not null)
        {
            orderByColumns.Add(
                $"{SqlServerIdentifier.Quote(sortColumn)} " +
                (sortDirection == SortDirection.Descending ? "DESC" : "ASC"));
        }

        if (primaryKeyColumns is not null)
        {
            foreach (var primaryKeyColumn in primaryKeyColumns)
            {
                if (string.Equals(primaryKeyColumn, sortColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orderByColumns.Add($"{SqlServerIdentifier.Quote(primaryKeyColumn)} ASC");
            }
        }

        var orderBy = orderByColumns.Count == 0
            ? "(SELECT NULL)"
            : string.Join(", ", orderByColumns);

        return $"SELECT * FROM {target} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
    }

    /// <summary>Builds a total row count query for a table or view.</summary>
    public static string BuildCountSql(string schema, string name)
        => $"SELECT COUNT_BIG(*) FROM {SqlServerIdentifier.QuoteQualified(schema, name)};";
}
