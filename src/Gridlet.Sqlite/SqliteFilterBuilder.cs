using System.Globalization;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>
/// Turns column filters into a SQLite WHERE clause. Column names are matched against the table's
/// own columns and quoted; values are always parameters.
/// </summary>
public static class SqliteFilterBuilder
{
    /// <summary>Builds the clause, including its leading <c>WHERE</c>, and its parameters.</summary>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) Build(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<ColumnInfo> columns)
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
                candidate => string.Equals(candidate.Name, filter.Column, StringComparison.OrdinalIgnoreCase))
                ?? throw new GridletValidationException(
                    $"Filter column '{filter.Column}' does not exist.");
            var quoted = SqliteIdentifier.Quote(column.Name);
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
                    $"Filter on '{column.Name}' needs a value. Use 'is null' to match rows without one.");
            var (predicate, parameterValue) = filter.Operator switch
            {
                FilterOperator.Equals => ($"{quoted} = {parameterName}", Bind(column, value)),
                FilterOperator.NotEquals => ($"{quoted} <> {parameterName}", Bind(column, value)),
                FilterOperator.LessThan => ($"{quoted} < {parameterName}", Bind(column, value)),
                FilterOperator.LessThanOrEqual => ($"{quoted} <= {parameterName}", Bind(column, value)),
                FilterOperator.GreaterThan => ($"{quoted} > {parameterName}", Bind(column, value)),
                FilterOperator.GreaterThanOrEqual => ($"{quoted} >= {parameterName}", Bind(column, value)),
                FilterOperator.Contains =>
                    ($"{quoted} LIKE {parameterName} ESCAPE '\\'", (object?)$"%{EscapeLike(value)}%"),
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

    /// <summary>
    /// Binds the value as the column's affinity would store it. SQLite compares across storage
    /// classes literally, so the text '5' does not equal the integer 5: without this, a filter on a
    /// numeric column would silently match nothing.
    /// </summary>
    private static object? Bind(ColumnInfo column, string value)
    {
        var declared = column.DataType.ToUpperInvariant();
        if (declared.Contains("INT", StringComparison.Ordinal))
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                ? integer
                : value;
        }

        var isNumericAffinity = declared.Contains("REAL", StringComparison.Ordinal)
            || declared.Contains("FLOA", StringComparison.Ordinal)
            || declared.Contains("DOUB", StringComparison.Ordinal)
            || declared.Contains("NUM", StringComparison.Ordinal)
            || declared.Contains("DEC", StringComparison.Ordinal);
        if (!isNumericAffinity)
        {
            return value;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            return whole;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
            ? real
            : value;
    }

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
}
