using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>
/// Turns column filters into a SQLite WHERE clause. Column names are matched against the table's
/// own columns and quoted; values are always parameters.
/// </summary>
public static class SqliteFilterBuilder
{
    /// <summary>
    /// The most values one request's filters may bind. SQLite would take more, but SQL Server stops
    /// near this number, and a filter that works on one provider should work on the other.
    /// </summary>
    private const int MaximumParameters = 2000;

    private const int MaximumDepth = 8;

    /// <summary>Builds the clause, including its leading <c>WHERE</c>, and its parameters.</summary>
    /// <remarks>
    /// Without the object's name the rankings and averages, which read the whole object, are
    /// unavailable. The overload that takes it is the one to use where it is known.
    /// </remarks>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) Build(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<ColumnInfo> columns)
        => BuildCore(filters, columns, target: null);

    /// <summary>
    /// Builds the clause, including its leading <c>WHERE</c>, and its parameters, for the named
    /// object, which the rankings and averages read.
    /// </summary>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) Build(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<ColumnInfo> columns,
        string schema,
        string name)
        => BuildCore(filters, columns, SqliteIdentifier.QuoteQualified(schema, name));

    /// <summary>
    /// Builds the same WHERE clause used by data reads with its parameters replaced by escaped
    /// SQLite literals, then the ORDER BY clause for the sort on a line of its own. Data reads never
    /// run it; it is shown above the grid and copied into the query editor.
    /// </summary>
    internal static string BuildFilterDisplaySql(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<ColumnInfo> columns,
        string schema,
        string name,
        string? sortColumn = null,
        SortDirection sortDirection = SortDirection.Ascending)
    {
        var (clause, parameters) = Build(filters, columns, schema, name);
        var sql = SubstituteFilterParameters(clause, parameters).TrimStart();
        if (string.IsNullOrEmpty(sortColumn))
        {
            return sql;
        }

        var sorted = columns.FirstOrDefault(
            column => string.Equals(column.Name, sortColumn, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletValidationException(
                $"Sort column '{sortColumn}' does not exist on {SqliteIdentifier.QuoteQualified(schema, name)}.");
        var orderBy = $"ORDER BY {SqliteIdentifier.Quote(sorted.Name)} "
            + (sortDirection == SortDirection.Descending ? "DESC" : "ASC");
        return sql.Length == 0 ? orderBy : sql + "\n" + orderBy;
    }

    /// <summary>Substitutes only complete <c>@fN</c> tokens, so <c>@f1</c> cannot alter <c>@f10</c>.</summary>
    internal static string SubstituteFilterParameters(
        string clause,
        IReadOnlyList<(string Name, object? Value)> parameters)
    {
        if (parameters.Count == 0) return clause;
        var values = parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value,
            StringComparer.Ordinal);
        return Regex.Replace(clause, @"@f\d+\b", match =>
            values.TryGetValue(match.Value, out var value) ? FilterDisplayLiteral(value) : match.Value,
            RegexOptions.CultureInvariant);
    }

    private static string FilterDisplayLiteral(object? value)
        => value switch
        {
            null or DBNull => "NULL",
            bool flag => flag ? "1" : "0",
            string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
            char character => $"'{character.ToString().Replace("'", "''", StringComparison.Ordinal)}'",
            DateTime date => $"'{date.ToString("O", CultureInfo.InvariantCulture)}'",
            DateTimeOffset date => $"'{date.ToString("O", CultureInfo.InvariantCulture)}'",
            DateOnly date => $"'{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'",
            TimeOnly time => $"'{time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)}'",
            TimeSpan time => $"'{time.ToString("c", CultureInfo.InvariantCulture)}'",
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => $"'{Convert.ToString(value, CultureInfo.InvariantCulture)!
                .Replace("'", "''", StringComparison.Ordinal)}'",
        };

    /// <summary>
    /// Builds the two statements behind a column filter's checklist: the distinct values that are not
    /// blank, in order and capped by <c>@limit</c>, then whether any row in scope is blank. Expects
    /// the parameters of <paramref name="whereClause"/>, <c>@limit</c>, and <c>@search</c> when
    /// <paramref name="search"/> is set.
    /// </summary>
    public static string BuildFilterValuesSql(
        string schema,
        string name,
        ColumnInfo column,
        string whereClause,
        bool search)
    {
        var target = SqliteIdentifier.QuoteQualified(schema, name);
        var quoted = SqliteIdentifier.Quote(column.Name);
        var scope = string.IsNullOrEmpty(whereClause) ? " WHERE " : whereClause + " AND ";
        return $"SELECT DISTINCT {quoted} FROM {target}{scope}{quoted} <> ''" +
            (search ? $" AND CAST({quoted} AS TEXT) LIKE @search ESCAPE '\\'" : "") +
            $" ORDER BY {quoted} LIMIT @limit;\n" +
            $"SELECT EXISTS (SELECT 1 FROM {target}{scope}({quoted} IS NULL OR {quoted} = ''));";
    }

    /// <summary>The <c>@search</c> pattern for <see cref="BuildFilterValuesSql"/>: values containing the text.</summary>
    public static string BuildFilterValuesSearch(string value) => $"%{EscapeLike(value)}%";

    private static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) BuildCore(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<ColumnInfo> columns,
        string? target)
    {
        if (filters is not { Count: > 0 })
        {
            return ("", []);
        }

        var predicates = new List<string>(filters.Count);
        var parameters = new List<(string Name, object? Value)>();
        foreach (var filter in filters)
        {
            predicates.Add(Predicate(filter, columns, target, parameters, depth: 0));
        }

        return (" WHERE " + string.Join(" AND ", predicates), parameters);
    }

    private static string Predicate(
        TableDataFilter filter,
        IReadOnlyList<ColumnInfo> columns,
        string? target,
        List<(string Name, object? Value)> parameters,
        int depth)
    {
        if (filter.Operator is FilterOperator.AnyOf or FilterOperator.AllOf)
        {
            if (depth >= MaximumDepth)
            {
                throw new GridletValidationException("Filter groups are nested too deeply.");
            }

            if (filter.Conditions is not { Count: > 0 })
            {
                throw new GridletValidationException("A filter group needs at least one condition.");
            }

            var conditions = filter.Conditions
                .Select(condition => Predicate(condition, columns, target, parameters, depth + 1))
                .ToArray();
            return conditions.Length == 1
                ? conditions[0]
                : "(" + string.Join(filter.Operator == FilterOperator.AnyOf ? " OR " : " AND ", conditions) + ")";
        }

        var column = columns.FirstOrDefault(
            candidate => string.Equals(candidate.Name, filter.Column, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletValidationException(
                $"Filter column '{filter.Column}' does not exist.");
        var quoted = SqliteIdentifier.Quote(column.Name);
        string Parameter(object? value) => AddParameter(parameters, value);

        switch (filter.Operator)
        {
            case FilterOperator.IsNull:
                return $"{quoted} IS NULL";
            case FilterOperator.IsNotNull:
                return $"{quoted} IS NOT NULL";
            case FilterOperator.IsBlank:
                return $"({quoted} IS NULL OR {quoted} = '')";
            case FilterOperator.IsNotBlank:
                return $"{quoted} <> ''";
            case FilterOperator.AboveAverage or FilterOperator.BelowAverage:
                // REAL on both sides: SQLite orders all text above every number, so a number kept in
                // a text column would otherwise always count as above the average.
                return $"CAST({quoted} AS REAL) {(filter.Operator == FilterOperator.AboveAverage ? ">" : "<")} " +
                    $"(SELECT AVG(CAST({quoted} AS REAL)) FROM {RequireTarget(filter, target)})";
            case FilterOperator.In or FilterOperator.NotIn:
                if (filter.Values is not { Count: > 0 })
                {
                    throw new GridletValidationException(
                        $"Filter on '{column.Name}' needs at least one value in its list.");
                }

                var list = string.Join(", ", filter.Values.Select(value => Parameter(Bind(column, value))));
                return $"{quoted} {(filter.Operator == FilterOperator.In ? "IN" : "NOT IN")} ({list})";
        }

        var value = filter.Value
            ?? throw new GridletValidationException(
                $"Filter on '{column.Name}' needs a value. Use 'is null' to match rows without one.");
        return filter.Operator switch
        {
            FilterOperator.Equals => $"{quoted} = {Parameter(Bind(column, value))}",
            FilterOperator.NotEquals => $"{quoted} <> {Parameter(Bind(column, value))}",
            FilterOperator.LessThan => $"{quoted} < {Parameter(Bind(column, value))}",
            FilterOperator.LessThanOrEqual => $"{quoted} <= {Parameter(Bind(column, value))}",
            FilterOperator.GreaterThan => $"{quoted} > {Parameter(Bind(column, value))}",
            FilterOperator.GreaterThanOrEqual => $"{quoted} >= {Parameter(Bind(column, value))}",
            FilterOperator.Contains => Like(quoted, "LIKE", Parameter($"%{EscapeLike(value)}%")),
            FilterOperator.NotContains => Like(quoted, "NOT LIKE", Parameter($"%{EscapeLike(value)}%")),
            FilterOperator.StartsWith => Like(quoted, "LIKE", Parameter($"{EscapeLike(value)}%")),
            FilterOperator.NotStartsWith => Like(quoted, "NOT LIKE", Parameter($"{EscapeLike(value)}%")),
            FilterOperator.EndsWith => Like(quoted, "LIKE", Parameter($"%{EscapeLike(value)}")),
            FilterOperator.NotEndsWith => Like(quoted, "NOT LIKE", Parameter($"%{EscapeLike(value)}")),
            FilterOperator.Matches => Like(quoted, "LIKE", Parameter(WildcardPattern(value))),
            FilterOperator.NotMatches => Like(quoted, "NOT LIKE", Parameter(WildcardPattern(value))),
            FilterOperator.Top or FilterOperator.Bottom or FilterOperator.TopPercent or FilterOperator.BottomPercent
                => RankPredicate(filter, column, quoted, target, value, parameters),
            FilterOperator.MonthEquals
                => $"CAST(strftime('%m', {quoted}) AS INTEGER) = {Parameter(DatePart(value, "month", 12))}",
            FilterOperator.QuarterEquals
                => $"(CAST(strftime('%m', {quoted}) AS INTEGER) + 2) / 3 = {Parameter(DatePart(value, "quarter", 4))}",
            _ => throw new GridletValidationException(
                $"Filter operator '{filter.Operator}' is not supported."),
        };
    }

    private static string Like(string quoted, string keyword, string parameterName)
        => $"{quoted} {keyword} {parameterName} ESCAPE '\\'";

    /// <summary>
    /// Keeps the rows at or beyond the value at the requested rank. Comparing with that value rather
    /// than taking the first rows keeps every row that ties with it, and the ranking reads the whole
    /// object rather than the rows other filters leave: both are how a spreadsheet's Top 10 counts.
    /// A percentage rounds up, as SQL Server's <c>TOP PERCENT</c> does.
    /// </summary>
    private static string RankPredicate(
        TableDataFilter filter,
        ColumnInfo column,
        string quoted,
        string? target,
        string value,
        List<(string Name, object? Value)> parameters)
    {
        var source = RequireTarget(filter, target);
        var percent = filter.Operator is FilterOperator.TopPercent or FilterOperator.BottomPercent;
        var maximum = percent ? 100 : 500;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count < 1 || count > maximum)
        {
            throw new GridletValidationException(
                $"Filter on '{column.Name}' needs a whole number from 1 to {maximum}.");
        }

        var top = filter.Operator is FilterOperator.Top or FilterOperator.TopPercent;
        var limit = percent
            ? $"(SELECT (COUNT({quoted}) * {AddParameter(parameters, (long)count)} + 99) / 100 FROM {source})"
            : AddParameter(parameters, (long)count);
        return $"{quoted} {(top ? ">=" : "<=")} (SELECT {(top ? "MIN" : "MAX")}(ranked_value) FROM " +
            $"(SELECT {quoted} AS ranked_value FROM {source} WHERE {quoted} IS NOT NULL " +
            $"ORDER BY {quoted} {(top ? "DESC" : "ASC")} LIMIT {limit}))";
    }

    private static long DatePart(string value, string part, int maximum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= maximum
                ? number
                : throw new GridletValidationException($"A {part} filter needs a number from 1 to {maximum}.");

    private static string RequireTarget(TableDataFilter filter, string? target)
        => target ?? throw new GridletValidationException(
            $"Filter operator '{filter.Operator}' needs the name of the object it reads.");

    private static string AddParameter(List<(string Name, object? Value)> parameters, object? value)
    {
        if (parameters.Count >= MaximumParameters)
        {
            throw new GridletValidationException(
                $"The filters compare more than {MaximumParameters} values. "
                + "Select fewer values, or use a condition such as 'contains'.");
        }

        var parameterName = "@f" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add((parameterName, value));
        return parameterName;
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

    /// <summary>
    /// Turns a spreadsheet pattern into a LIKE pattern: <c>*</c> and <c>?</c> become <c>%</c> and
    /// <c>_</c>, <c>~</c> makes the character after it literal, and every other character, LIKE's
    /// own wildcards included, matches itself.
    /// </summary>
    private static string WildcardPattern(string value)
    {
        var pattern = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '~' && index + 1 < value.Length)
            {
                pattern.Append(EscapeLike(value[++index].ToString()));
                continue;
            }

            pattern.Append(character switch
            {
                '*' => "%",
                '?' => "_",
                _ => EscapeLike(character.ToString()),
            });
        }

        return pattern.ToString();
    }

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
}
