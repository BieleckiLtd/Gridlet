using System.Text;

namespace Gridlet.Sqlite;

/// <summary>Small, conservative lexical helpers for inspecting SQLite schema SQL.</summary>
internal static class SqliteSqlInspection
{
    public static bool ContainsKeyword(string? sql, string keyword)
        => Tokens(sql).Any(token => string.Equals(token, keyword, StringComparison.OrdinalIgnoreCase));

    public static bool ContainsKeywordSequence(string? sql, string first, string second)
    {
        var previous = "";
        foreach (var token in Tokens(sql))
        {
            if (string.Equals(previous, first, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(token, second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            previous = token;
        }

        return false;
    }

    public static bool HasAutoincrementColumn(string? createSql, string columnName)
    {
        if (string.IsNullOrWhiteSpace(createSql)) return false;

        foreach (var definition in TableDefinitions(createSql))
        {
            var (name, consumed) = ReadLeadingIdentifier(definition);
            if (name is null || !string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ContainsKeyword(definition[consumed..], "AUTOINCREMENT");
        }

        return false;
    }

    private static IEnumerable<string> Tokens(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) yield break;

        for (var i = 0; i < sql.Length;)
        {
            if (TrySkipQuotedOrComment(sql, ref i)) continue;

            if (char.IsLetter(sql[i]) || sql[i] == '_')
            {
                var start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                yield return sql[start..i];
                continue;
            }

            i++;
        }
    }

    private static IEnumerable<string> TableDefinitions(string sql)
    {
        var open = -1;
        for (var i = 0; i < sql.Length;)
        {
            if (TrySkipQuotedOrComment(sql, ref i)) continue;
            if (sql[i] == '(')
            {
                open = i;
                break;
            }

            i++;
        }

        if (open < 0) yield break;

        var depth = 1;
        var start = open + 1;
        for (var i = start; i < sql.Length;)
        {
            if (TrySkipQuotedOrComment(sql, ref i)) continue;

            switch (sql[i])
            {
                case '(':
                    depth++;
                    i++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        yield return sql[start..i].Trim();
                        yield break;
                    }
                    i++;
                    break;
                case ',' when depth == 1:
                    yield return sql[start..i].Trim();
                    start = ++i;
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    private static (string? Name, int Consumed) ReadLeadingIdentifier(string value)
    {
        var i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
        if (i == value.Length) return (null, i);

        var start = i;
        if (value[i] is '\'' or '"' or '`')
        {
            var delimiter = value[i++];
            var result = new StringBuilder();
            while (i < value.Length)
            {
                if (value[i] == delimiter)
                {
                    if (i + 1 < value.Length && value[i + 1] == delimiter)
                    {
                        result.Append(delimiter);
                        i += 2;
                        continue;
                    }

                    return (result.ToString(), i + 1);
                }

                result.Append(value[i++]);
            }

            return (null, start);
        }

        if (value[i] == '[')
        {
            var close = value.IndexOf(']', i + 1);
            return close < 0
                ? (null, start)
                : (value[(i + 1)..close], close + 1);
        }

        while (i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] is not '(' and not ')' and not ',') i++;
        return i == start ? (null, start) : (value[start..i], i);
    }

    private static bool TrySkipQuotedOrComment(string sql, ref int index)
    {
        if (index >= sql.Length) return false;

        if (sql[index] == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
        {
            index += 2;
            while (index < sql.Length && sql[index] is not '\r' and not '\n') index++;
            return true;
        }

        if (sql[index] == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
        {
            var close = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
            index = close < 0 ? sql.Length : close + 2;
            return true;
        }

        if (sql[index] is not ('\'' or '"' or '`' or '[')) return false;

        var delimiter = sql[index] == '[' ? ']' : sql[index];
        index++;
        while (index < sql.Length)
        {
            if (sql[index] == delimiter)
            {
                if (delimiter != ']' && index + 1 < sql.Length && sql[index + 1] == delimiter)
                {
                    index += 2;
                    continue;
                }

                index++;
                return true;
            }

            index++;
        }

        return true;
    }
}
