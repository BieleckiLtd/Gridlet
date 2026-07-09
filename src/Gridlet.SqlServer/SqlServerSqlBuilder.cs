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
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, name);
        var orderBy = sortColumn is null
            ? "(SELECT NULL)"
            : $"{SqlServerIdentifier.Quote(sortColumn)} {(sortDirection == SortDirection.Descending ? "DESC" : "ASC")}";

        return $"SELECT * FROM {target} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
    }

    /// <summary>Builds a total row count query for a table or view.</summary>
    public static string BuildCountSql(string schema, string name)
        => $"SELECT COUNT_BIG(*) FROM {SqlServerIdentifier.QuoteQualified(schema, name)};";
}
