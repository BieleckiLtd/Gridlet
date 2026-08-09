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
        => BuildPageSql(schema, name, sortColumn, sortDirection, primaryKeyColumns, whereClause: "");

    /// <summary>Builds a paged <c>SELECT</c> restricted by <paramref name="whereClause"/>.</summary>
    public static string BuildPageSql(
        string schema,
        string name,
        string? sortColumn,
        SortDirection sortDirection,
        IReadOnlyList<string>? primaryKeyColumns,
        string whereClause)
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

        return $"SELECT * FROM {target}{whereClause} ORDER BY {orderBy} " +
            "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
    }

    /// <summary>Builds a total row count query for a table or view.</summary>
    public static string BuildCountSql(string schema, string name)
        => BuildCountSql(schema, name, whereClause: "");

    /// <summary>
    /// Builds a total row count restricted by <paramref name="whereClause"/>, so the total matches
    /// the rows the same filter returns.
    /// </summary>
    public static string BuildCountSql(string schema, string name, string whereClause)
        => $"SELECT COUNT_BIG(*) FROM {SqlServerIdentifier.QuoteQualified(schema, name)}{whereClause};";

    /// <summary>
    /// Translates column filters into a WHERE clause and its parameters. Column names are matched
    /// against <paramref name="columns"/> and then bracket-quoted; every value is a parameter, so a
    /// filter cannot carry SQL.
    /// </summary>
    /// <param name="filters">The conditions, combined with AND. Null or empty yields no clause.</param>
    /// <param name="columns">The object's column names, used to resolve and validate each filter.</param>
    /// <returns>
    /// The clause including its leading <c>WHERE</c>, or an empty string, and the parameters to add
    /// to the command.
    /// </returns>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) BuildFilterClause(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<string> columns)
    {
        if (filters is not { Count: > 0 })
        {
            return ("", []);
        }

        var predicates = new List<string>(filters.Count);
        var parameters = new List<(string, object?)>();
        foreach (var filter in filters)
        {
            var column = columns.FirstOrDefault(
                candidate => string.Equals(candidate, filter.Column, StringComparison.OrdinalIgnoreCase))
                ?? throw new GridletValidationException(
                    $"Filter column '{filter.Column}' does not exist.");
            var quoted = SqlServerIdentifier.Quote(column);
            var parameterName = "@f" + parameters.Count;

            switch (filter.Operator)
            {
                case FilterOperator.IsNull:
                    predicates.Add($"{quoted} IS NULL");
                    continue;
                case FilterOperator.IsNotNull:
                    predicates.Add($"{quoted} IS NOT NULL");
                    continue;
            }

            var value = filter.Value
                ?? throw new GridletValidationException(
                    $"Filter on '{column}' needs a value. Use 'is null' to match rows without one.");
            var (predicate, parameterValue) = filter.Operator switch
            {
                FilterOperator.Equals => ($"{quoted} = {parameterName}", (object?)value),
                FilterOperator.NotEquals => ($"{quoted} <> {parameterName}", value),
                FilterOperator.LessThan => ($"{quoted} < {parameterName}", value),
                FilterOperator.LessThanOrEqual => ($"{quoted} <= {parameterName}", value),
                FilterOperator.GreaterThan => ($"{quoted} > {parameterName}", value),
                FilterOperator.GreaterThanOrEqual => ($"{quoted} >= {parameterName}", value),
                FilterOperator.Contains =>
                    ($"{quoted} LIKE {parameterName} ESCAPE '\\'", $"%{EscapeLike(value)}%"),
                FilterOperator.NotContains =>
                    ($"{quoted} NOT LIKE {parameterName} ESCAPE '\\'", $"%{EscapeLike(value)}%"),
                FilterOperator.StartsWith =>
                    ($"{quoted} LIKE {parameterName} ESCAPE '\\'", $"{EscapeLike(value)}%"),
                FilterOperator.EndsWith =>
                    ($"{quoted} LIKE {parameterName} ESCAPE '\\'", $"%{EscapeLike(value)}"),
                _ => throw new GridletValidationException(
                    $"Filter operator '{filter.Operator}' is not supported."),
            };

            predicates.Add(predicate);
            parameters.Add((parameterName, parameterValue));
        }

        return (" WHERE " + string.Join(" AND ", predicates), parameters);
    }

    /// <summary>Escapes the characters LIKE treats as wildcards, including the escape character itself.</summary>
    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");
}
