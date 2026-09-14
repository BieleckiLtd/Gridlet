using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>A column a filter may name, with the SQL Server system type that decides how its values bind.</summary>
/// <param name="Name">The column's name as the object declares it.</param>
/// <param name="SystemType">
/// The column's system type name, such as <c>int</c> or <c>nvarchar</c>, or <see langword="null"/>
/// when it is unknown and values are left to SQL Server's own conversion.
/// </param>
public sealed record SqlServerFilterColumn(string Name, string? SystemType);

public static partial class SqlServerSqlBuilder
{
    /// <summary>
    /// The most values one request's filters may bind. SQL Server accepts 2,100 parameters in a
    /// statement, and the paging statement needs a few of its own.
    /// </summary>
    private const int MaximumFilterParameters = 2000;

    private const int MaximumFilterDepth = 8;

    private static readonly HashSet<string> FilterTextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "nchar", "varchar", "nvarchar", "text", "ntext",
    };

    private static readonly HashSet<string> FilterWholeNumberTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint",
    };

    private static readonly HashSet<string> FilterExactNumberTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "decimal", "numeric", "money", "smallmoney",
    };

    private static readonly HashSet<string> FilterDateTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "datetime", "datetime2", "smalldatetime",
    };

    /// <summary>
    /// Translates column filters into a WHERE clause and its parameters. Column names are matched
    /// against <paramref name="columns"/> and then bracket-quoted; every value is a parameter, so a
    /// filter cannot carry SQL.
    /// </summary>
    /// <remarks>
    /// Without column types every value binds as text for SQL Server to convert, and without the
    /// object's name the rankings and averages, which read the whole object, are unavailable. The
    /// overload that takes both is the one to use where they are known.
    /// </remarks>
    /// <param name="filters">The conditions, combined with AND. Null or empty yields no clause.</param>
    /// <param name="columns">The object's column names, used to resolve and validate each filter.</param>
    /// <returns>
    /// The clause including its leading <c>WHERE</c>, or an empty string, and the parameters to add
    /// to the command.
    /// </returns>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) BuildFilterClause(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<string> columns)
        => BuildFilterClauseCore(
            filters, columns.Select(column => new SqlServerFilterColumn(column, null)).ToArray(), target: null);

    /// <summary>
    /// Translates column filters on the named object into a WHERE clause and its parameters, binding
    /// each value as its column's type.
    /// </summary>
    /// <param name="filters">The conditions, combined with AND. Null or empty yields no clause.</param>
    /// <param name="columns">The object's columns and their system types.</param>
    /// <param name="schema">The object's schema, which the rankings and averages read.</param>
    /// <param name="name">The object's name.</param>
    public static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) BuildFilterClause(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<SqlServerFilterColumn> columns,
        string schema,
        string name)
        => BuildFilterClauseCore(filters, columns, SqlServerIdentifier.QuoteQualified(schema, name));

    /// <summary>
    /// Builds the same WHERE clause used by data reads, then replaces its parameters with escaped
    /// SQL Server literals for a read-only UI description. The returned SQL is never executed.
    /// </summary>
    internal static string BuildFilterDisplaySql(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<SqlServerFilterColumn> columns,
        string schema,
        string name)
    {
        var (clause, parameters) = BuildFilterClause(filters, columns, schema, name);
        return SubstituteFilterParameters(clause, parameters).TrimStart();
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
            string text => $"N'{text.Replace("'", "''", StringComparison.Ordinal)}'",
            char character => $"N'{character.ToString().Replace("'", "''", StringComparison.Ordinal)}'",
            DateTime date => $"'{FilterDisplayDate(date)}'",
            DateTimeOffset date => $"'{date.ToString("O", CultureInfo.InvariantCulture)}'",
            DateOnly date => $"'{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'",
            TimeOnly time => $"'{time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)}'",
            TimeSpan time => $"'{time.ToString("c", CultureInfo.InvariantCulture)}'",
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => $"N'{Convert.ToString(value, CultureInfo.InvariantCulture)!
                .Replace("'", "''", StringComparison.Ordinal)}'",
        };

    /// <summary>
    /// A date as ISO text every SQL Server date type reads, because "Use in query" runs the clause.
    /// The fraction is left out when there is none and kept to milliseconds where that is exact:
    /// datetime rejects more than three fractional digits, and only a datetime2 value, which
    /// accepts seven, has finer ticks.
    /// </summary>
    private static string FilterDisplayDate(DateTime date)
        => date.ToString(
            date.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss"
            : date.Ticks % TimeSpan.TicksPerMillisecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss.fff"
            : "yyyy-MM-dd'T'HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the two statements behind a column filter's checklist: the distinct values that are not
    /// blank, in order and capped by <c>@limit</c>, then whether any row in scope is blank. Expects
    /// the parameters of <paramref name="whereClause"/>, <c>@limit</c>, and <c>@search</c> when
    /// <paramref name="search"/> is set.
    /// </summary>
    public static string BuildFilterValuesSql(
        string schema,
        string name,
        SqlServerFilterColumn column,
        string whereClause,
        bool search)
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, name);
        var quoted = SqlServerIdentifier.Quote(column.Name);
        var scope = string.IsNullOrEmpty(whereClause) ? " WHERE " : whereClause + " AND ";
        return $"SELECT DISTINCT TOP (@limit) {quoted} FROM {target}{scope}{NotBlankPredicate(quoted, column.SystemType)}" +
            (search ? $" AND CONVERT(nvarchar(max), {quoted}) LIKE @search" : "") +
            $" ORDER BY {quoted};\n" +
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {target}{scope}{BlankPredicate(quoted, column.SystemType)}) " +
            "THEN 1 ELSE 0 END;";
    }

    /// <summary>The <c>@search</c> pattern for <see cref="BuildFilterValuesSql"/>: values containing the text.</summary>
    public static string BuildFilterValuesSearch(string value) => $"%{EscapeLike(value)}%";

    private static (string Clause, IReadOnlyList<(string Name, object? Value)> Parameters) BuildFilterClauseCore(
        IReadOnlyList<TableDataFilter>? filters,
        IReadOnlyList<SqlServerFilterColumn> columns,
        string? target)
    {
        if (filters is not { Count: > 0 })
        {
            return ("", []);
        }

        var parameters = new List<(string Name, object? Value)>();
        var predicates = new List<string>(filters.Count);
        foreach (var filter in filters)
        {
            predicates.Add(FilterPredicate(filter, columns, target, parameters, depth: 0));
        }

        return (" WHERE " + string.Join(" AND ", predicates), parameters);
    }

    private static string FilterPredicate(
        TableDataFilter filter,
        IReadOnlyList<SqlServerFilterColumn> columns,
        string? target,
        List<(string Name, object? Value)> parameters,
        int depth)
    {
        if (filter.Operator is FilterOperator.AnyOf or FilterOperator.AllOf)
        {
            if (depth >= MaximumFilterDepth)
            {
                throw new GridletValidationException("Filter groups are nested too deeply.");
            }

            if (filter.Conditions is not { Count: > 0 })
            {
                throw new GridletValidationException("A filter group needs at least one condition.");
            }

            var conditions = filter.Conditions
                .Select(condition => FilterPredicate(condition, columns, target, parameters, depth + 1))
                .ToArray();
            return conditions.Length == 1
                ? conditions[0]
                : "(" + string.Join(filter.Operator == FilterOperator.AnyOf ? " OR " : " AND ", conditions) + ")";
        }

        var column = columns.FirstOrDefault(
            candidate => string.Equals(candidate.Name, filter.Column, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletValidationException(
                $"Filter column '{filter.Column}' does not exist.");
        var quoted = SqlServerIdentifier.Quote(column.Name);
        string Parameter(object? value) => AddFilterParameter(parameters, value);

        switch (filter.Operator)
        {
            case FilterOperator.IsNull:
                return $"{quoted} IS NULL";
            case FilterOperator.IsNotNull:
                return $"{quoted} IS NOT NULL";
            case FilterOperator.IsBlank:
                return BlankPredicate(quoted, column.SystemType);
            case FilterOperator.IsNotBlank:
                return NotBlankPredicate(quoted, column.SystemType);
            case FilterOperator.AboveAverage or FilterOperator.BelowAverage:
                RequireWholeObject(filter, target);
                if (column.SystemType is not null && !IsFilterNumberType(column.SystemType))
                {
                    throw new GridletValidationException(
                        $"An average needs a number column, and '{column.Name}' is {column.SystemType}.");
                }

                // float keeps the average of an int column from being truncated to a whole number.
                return $"CONVERT(float, {quoted}) {(filter.Operator == FilterOperator.AboveAverage ? ">" : "<")} " +
                    $"(SELECT AVG(CONVERT(float, {quoted})) FROM {target})";
            case FilterOperator.In or FilterOperator.NotIn:
                if (filter.Values is not { Count: > 0 })
                {
                    throw new GridletValidationException(
                        $"Filter on '{column.Name}' needs at least one value in its list.");
                }

                var list = string.Join(", ", filter.Values.Select(value => Parameter(BindFilterValue(column, value))));
                return $"{quoted} {(filter.Operator == FilterOperator.In ? "IN" : "NOT IN")} ({list})";
        }

        var value = filter.Value
            ?? throw new GridletValidationException(
                $"Filter on '{column.Name}' needs a value. Use 'is null' to match rows without one.");
        return filter.Operator switch
        {
            FilterOperator.Equals => $"{quoted} = {Parameter(BindFilterValue(column, value))}",
            FilterOperator.NotEquals => $"{quoted} <> {Parameter(BindFilterValue(column, value))}",
            FilterOperator.LessThan => $"{quoted} < {Parameter(BindFilterValue(column, value))}",
            FilterOperator.LessThanOrEqual => $"{quoted} <= {Parameter(BindFilterValue(column, value))}",
            FilterOperator.GreaterThan => $"{quoted} > {Parameter(BindFilterValue(column, value))}",
            FilterOperator.GreaterThanOrEqual => $"{quoted} >= {Parameter(BindFilterValue(column, value))}",
            FilterOperator.Contains => $"{quoted} LIKE {Parameter($"%{EscapeLike(value)}%")}",
            FilterOperator.NotContains => $"{quoted} NOT LIKE {Parameter($"%{EscapeLike(value)}%")}",
            FilterOperator.StartsWith => $"{quoted} LIKE {Parameter($"{EscapeLike(value)}%")}",
            FilterOperator.NotStartsWith => $"{quoted} NOT LIKE {Parameter($"{EscapeLike(value)}%")}",
            FilterOperator.EndsWith => $"{quoted} LIKE {Parameter($"%{EscapeLike(value)}")}",
            FilterOperator.NotEndsWith => $"{quoted} NOT LIKE {Parameter($"%{EscapeLike(value)}")}",
            FilterOperator.Matches => $"{quoted} LIKE {Parameter(WildcardPattern(value))}",
            FilterOperator.NotMatches => $"{quoted} NOT LIKE {Parameter(WildcardPattern(value))}",
            FilterOperator.Top or FilterOperator.Bottom or FilterOperator.TopPercent or FilterOperator.BottomPercent
                => RankPredicate(filter, column, quoted, target, value, parameters),
            FilterOperator.MonthEquals
                => $"MONTH({quoted}) = {Parameter(DatePart(column, value, "month", 12))}",
            FilterOperator.QuarterEquals
                => $"DATEPART(quarter, {quoted}) = {Parameter(DatePart(column, value, "quarter", 4))}",
            _ => throw new GridletValidationException(
                $"Filter operator '{filter.Operator}' is not supported."),
        };
    }

    /// <summary>
    /// Keeps the rows at or beyond the value at the requested rank. Comparing with that value rather
    /// than taking the first rows keeps every row that ties with it, and the ranking reads the whole
    /// object rather than the rows other filters leave: both are how a spreadsheet's Top 10 counts.
    /// </summary>
    private static string RankPredicate(
        TableDataFilter filter,
        SqlServerFilterColumn column,
        string quoted,
        string? target,
        string value,
        List<(string Name, object? Value)> parameters)
    {
        RequireWholeObject(filter, target);
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
            ? $"TOP ({AddFilterParameter(parameters, (double)count)}) PERCENT"
            : $"TOP ({AddFilterParameter(parameters, (long)count)})";
        return $"{quoted} {(top ? ">=" : "<=")} (SELECT {(top ? "MIN" : "MAX")}([ranked].[value]) FROM " +
            $"(SELECT {limit} {quoted} AS [value] FROM {target} WHERE {quoted} IS NOT NULL " +
            $"ORDER BY {quoted} {(top ? "DESC" : "ASC")}) AS [ranked])";
    }

    private static int DatePart(SqlServerFilterColumn column, string value, string part, int maximum)
    {
        if (column.SystemType is not null
            && (IsFilterNumberType(column.SystemType)
                || string.Equals(column.SystemType, "time", StringComparison.OrdinalIgnoreCase)))
        {
            throw new GridletValidationException(
                $"A {part} filter needs a date column, and '{column.Name}' is {column.SystemType}.");
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= maximum
                ? number
                : throw new GridletValidationException($"A {part} filter needs a number from 1 to {maximum}.");
    }

    private static void RequireWholeObject(TableDataFilter filter, string? target)
    {
        if (target is null)
        {
            throw new GridletValidationException(
                $"Filter operator '{filter.Operator}' needs the name of the object it reads.");
        }
    }

    /// <summary>
    /// Binds the value as its column's kind of value, so SQL Server compares like with like: a number
    /// as a number, which lets 1.5 compare against an int column the text '1.5' cannot be converted
    /// to, and a date as a date, which the text '2026-01-02' is not under every language setting. A
    /// column of any other type, or of an unknown one, gets the text and SQL Server's own conversion.
    /// </summary>
    private static object BindFilterValue(SqlServerFilterColumn column, string value)
    {
        var type = column.SystemType;
        if (type is null)
        {
            return value;
        }

        var text = value.Trim();
        if (FilterWholeNumberTypes.Contains(type)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            return whole;
        }

        if (FilterWholeNumberTypes.Contains(type) || FilterExactNumberTypes.Contains(type))
        {
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
                ? exact
                : throw NotAFilterValue(column, value, "number");
        }

        if (type.Equals("float", StringComparison.OrdinalIgnoreCase) || type.Equals("real", StringComparison.OrdinalIgnoreCase))
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                ? real
                : throw NotAFilterValue(column, value, "number");
        }

        if (type.Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            return bool.TryParse(text, out var flag) ? flag
                : text == "1" ? true
                : text == "0" ? false
                : throw NotAFilterValue(column, value, "true or false value");
        }

        if (FilterDateTypes.Contains(type))
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                ? date
                : throw NotAFilterValue(column, value, "date");
        }

        if (type.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
                ? moment
                : throw NotAFilterValue(column, value, "date");
        }

        if (type.Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time)
                ? time
                : throw NotAFilterValue(column, value, "time");
        }

        return value;
    }

    private static GridletValidationException NotAFilterValue(SqlServerFilterColumn column, string value, string kind)
        => new($"'{value}' is not a {kind}, which column '{column.Name}' needs.");

    private static bool IsFilterNumberType(string systemType)
        => FilterWholeNumberTypes.Contains(systemType)
            || FilterExactNumberTypes.Contains(systemType)
            || systemType.Equals("float", StringComparison.OrdinalIgnoreCase)
            || systemType.Equals("real", StringComparison.OrdinalIgnoreCase)
            || systemType.Equals("bit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A blank is a null or, in a text column, a value with no characters at all. DATALENGTH rather
    /// than <c>= N''</c>, which SQL Server would also answer true for a value of spaces, and which
    /// the legacy text and ntext types cannot be compared with.
    /// </summary>
    private static string BlankPredicate(string quoted, string? systemType)
        => systemType is not null && FilterTextTypes.Contains(systemType)
            ? $"({quoted} IS NULL OR DATALENGTH({quoted}) = 0)"
            : $"{quoted} IS NULL";

    private static string NotBlankPredicate(string quoted, string? systemType)
        => systemType is not null && FilterTextTypes.Contains(systemType)
            ? $"DATALENGTH({quoted}) > 0"
            : $"{quoted} IS NOT NULL";

    private static string AddFilterParameter(List<(string Name, object? Value)> parameters, object? value)
    {
        if (parameters.Count >= MaximumFilterParameters)
        {
            throw new GridletValidationException(
                $"The filters compare more than {MaximumFilterParameters} values. "
                + "Select fewer values, or use a condition such as 'contains'.");
        }

        var parameterName = "@f" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add((parameterName, value));
        return parameterName;
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

    /// <summary>
    /// Escapes the characters LIKE treats as wildcards by putting each in a character class, which
    /// is the form SQL Server documents for matching one literally.
    /// </summary>
    /// <remarks>
    /// A class is used rather than an ESCAPE character because it is unambiguous for <c>[</c>, which
    /// opens a class of its own: <c>[[]</c> matches one literal bracket whatever the escape rules
    /// say. It also leaves a backslash in the search text as an ordinary character rather than a
    /// second thing to escape. The bracket is replaced first, since the other replacements introduce
    /// brackets of their own.
    /// </remarks>
    private static string EscapeLike(string value)
        => value
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");
}
