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
    /// Binds the value the way SQLite would read the same literal in a comparison against this
    /// column, which is the behaviour somebody typing the condition into the query editor would get.
    /// </summary>
    /// <remarks>
    /// A column with a numeric affinity needs no help: SQLite converts an untyped parameter to that
    /// affinity before comparing, so the text '5' does find the integer 5. A column with no declared
    /// type has no affinity, nothing is converted, and the same filter silently matches nothing -
    /// which is what this is for. Only TEXT affinity is left alone, because there the conversion runs
    /// the other way and binding the number 7 would stop the filter '007' from matching the text it
    /// was written for.
    /// </remarks>
    private static object? Bind(ColumnInfo column, string value)
    {
        var declared = column.DataType.ToUpperInvariant();

        // SQLite's own rule order: a declared type containing INT is INTEGER affinity whatever else
        // it contains, so it is checked before the text names.
        var isText = !declared.Contains("INT", StringComparison.Ordinal)
            && (declared.Contains("CHAR", StringComparison.Ordinal)
                || declared.Contains("CLOB", StringComparison.Ordinal)
                || declared.Contains("TEXT", StringComparison.Ordinal));

        return isText ? value : AsNumber(value);
    }

    /// <summary>
    /// Converts the value the way a numeric affinity stores one: as an integer where it is whole, as
    /// a real where it is not, and unchanged where it is not a number at all - a date held as text
    /// in a DATE column keeps comparing as the text it is.
    /// </summary>
    private static object AsNumber(string value)
    {
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
