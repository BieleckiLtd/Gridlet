using System.Text;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>
/// Small, SQLite-aware structural parser for the portions of CREATE TABLE/INDEX statements that
/// pragmas do not expose. It deliberately tokenizes instead of using regular expressions so
/// commas and parentheses inside strings, quoted identifiers, expressions, and comments are safe.
/// </summary>
internal static class SqliteCreateSqlParser
{
    internal sealed record ParsedTable(
        IReadOnlyList<CheckConstraintInfo> Checks,
        IReadOnlyList<UniqueConstraintInfo> Uniques,
        IReadOnlyDictionary<string, string> ColumnCollations);

    internal sealed record ParsedIndex(
        IReadOnlyList<IndexKeyInfo> Keys,
        string? Filter);

    private sealed record Token(string Text, int Start, int End, TokenKind Kind, int Depth);
    private enum TokenKind { Word, Identifier, String, Symbol }

    public static ParsedTable ParseTable(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || !TryFindParenthesizedBody(sql, out var bodyStart, out var bodyEnd))
        {
            return new ParsedTable([], [], new Dictionary<string, string>());
        }

        var checks = new List<CheckConstraintInfo>();
        var uniques = new List<UniqueConstraintInfo>();
        var columnCollations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fragment in SplitTopLevel(sql[(bodyStart + 1)..bodyEnd]))
        {
            ParseTableFragment(fragment, checks, uniques, columnCollations);
        }

        return new ParsedTable(
            checks.Select((item, ordinal) => item with { Ordinal = ordinal }).ToArray(),
            uniques.Select((item, ordinal) => item with { Ordinal = ordinal }).ToArray(),
            columnCollations);
    }

    public static ParsedIndex ParseIndex(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return new ParsedIndex([], null);
        var tokens = Tokenize(sql);
        var on = tokens.FindIndex(t => t.Depth == 0 && IsWord(t, "ON"));
        if (on < 0) return new ParsedIndex([], null);

        var open = tokens.FindIndex(on + 1, t => t.Depth == 0 && t.Text == "(");
        if (open < 0 || !TryFindMatchingParenthesis(sql, tokens[open].Start, out var close))
        {
            return new ParsedIndex([], null);
        }

        var keys = SplitTopLevel(sql[(tokens[open].Start + 1)..close])
            .Select((part, ordinal) => ParseIndexKey(part, ordinal + 1, allowSingleQuotedIdentifier: false))
            .ToArray();
        var tail = sql[(close + 1)..];
        var tailTokens = Tokenize(tail);
        var where = tailTokens.FindIndex(t => t.Depth == 0 && IsWord(t, "WHERE"));
        var filter = where < 0 ? null : tail[(tailTokens[where].End)..].Trim().TrimEnd(';').Trim();
        return new ParsedIndex(keys, string.IsNullOrWhiteSpace(filter) ? null : filter);
    }

    public static string RenameIdentifier(string expression, string oldName, string newName)
    {
        var tokens = Tokenize(expression);
        var builder = new StringBuilder(expression.Length + 16);
        var previous = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind is not (TokenKind.Word or TokenKind.Identifier) ||
                !string.Equals(UnquoteIdentifier(token.Text), oldName, StringComparison.OrdinalIgnoreCase) ||
                index + 1 < tokens.Count && tokens[index + 1].Text == "(")
            {
                continue;
            }

            builder.Append(expression, previous, token.Start - previous);
            builder.Append(SqliteIdentifier.Quote(newName));
            previous = token.End;
        }

        builder.Append(expression, previous, expression.Length - previous);
        return builder.ToString();
    }

    public static string RemoveComments(string expression)
    {
        var builder = new StringBuilder(expression.Length);
        for (var i = 0; i < expression.Length;)
        {
            if (expression[i] == '-' && i + 1 < expression.Length && expression[i + 1] == '-')
            {
                builder.Append(' ');
                i += 2;
                while (i < expression.Length && expression[i] is not ('\r' or '\n')) i++;
                continue;
            }
            if (expression[i] == '/' && i + 1 < expression.Length && expression[i + 1] == '*')
            {
                builder.Append(' ');
                i += 2;
                while (i + 1 < expression.Length && !(expression[i] == '*' && expression[i + 1] == '/')) i++;
                i = Math.Min(expression.Length, i + 2);
                continue;
            }
            if (expression[i] is '\'' or '"' or '`' or '[')
            {
                var start = i;
                var open = expression[i];
                ReadQuoted(expression, ref i, open, open == '[' ? ']' : open);
                builder.Append(expression, start, i - start);
                continue;
            }
            builder.Append(expression[i++]);
        }
        return builder.ToString();
    }

    /// <param name="columnCollations">Receives the declared collation of each column that has one.</param>
    private static void ParseTableFragment(
        string rawFragment,
        List<CheckConstraintInfo> checks,
        List<UniqueConstraintInfo> uniques,
        Dictionary<string, string> columnCollations)
    {
        var fragment = rawFragment.Trim();
        if (fragment.Length == 0) return;
        var tokens = Tokenize(fragment);
        if (tokens.Count == 0) return;

        var position = 0;
        string? tableConstraintName = null;
        if (IsWord(tokens[position], "CONSTRAINT") && tokens.Count > position + 1)
        {
            tableConstraintName = UnquoteIdentifier(tokens[position + 1].Text);
            position += 2;
        }

        if (position < tokens.Count && IsWord(tokens[position], "CHECK"))
        {
            var expression = ExtractParenthesizedAfter(fragment, tokens[position]);
            if (expression is not null) checks.Add(new CheckConstraintInfo(tableConstraintName, expression));
            return;
        }

        if (position < tokens.Count && IsWord(tokens[position], "UNIQUE"))
        {
            var body = ExtractParenthesizedAfter(fragment, tokens[position]);
            if (body is not null)
            {
                uniques.Add(new UniqueConstraintInfo(tableConstraintName,
                    SplitTopLevel(body).Select((part, ordinal) =>
                        ParseIndexKey(part, ordinal + 1, allowSingleQuotedIdentifier: true)).ToArray()));
            }
            return;
        }

        // PRIMARY KEY and FOREIGN KEY are table constraints, not column definitions.
        if (position < tokens.Count && (IsWord(tokens[position], "PRIMARY") || IsWord(tokens[position], "FOREIGN")))
        {
            return;
        }

        var columnName = UnquoteIdentifier(tokens[0].Text);
        string? pendingConstraintName = null;
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Depth != 0) continue;
            if (IsWord(token, "CONSTRAINT") && i + 1 < tokens.Count && tokens[i + 1].Depth == 0)
            {
                pendingConstraintName = UnquoteIdentifier(tokens[++i].Text);
                continue;
            }

            if (IsWord(token, "CHECK"))
            {
                var expression = ExtractParenthesizedAfter(fragment, token);
                if (expression is not null)
                {
                    checks.Add(new CheckConstraintInfo(pendingConstraintName, expression, Column: columnName));
                }
                pendingConstraintName = null;
            }
            else if (IsWord(token, "UNIQUE"))
            {
                uniques.Add(new UniqueConstraintInfo(pendingConstraintName,
                    [new IndexKeyInfo(columnName, 1)]));
                pendingConstraintName = null;
            }
            else if (pendingConstraintName is not null && token.Kind == TokenKind.Word)
            {
                // A CONSTRAINT name belongs only to the immediately following column constraint.
                pendingConstraintName = null;
            }
            if (IsWord(token, "COLLATE"))
            {
                // The collation name is the token straight after COLLATE, which is where SQLite
                // reads it from too.
                if (i + 1 < tokens.Count && tokens[i + 1].Depth == 0)
                {
                    columnCollations[columnName] = UnquoteIdentifier(tokens[i + 1].Text);
                }
            }
        }
    }

    private static IndexKeyInfo ParseIndexKey(string raw, int ordinal, bool allowSingleQuotedIdentifier)
    {
        var text = raw.Trim();
        var tokens = Tokenize(text);
        var descending = false;
        string? collation = null;
        var end = text.Length;

        if (tokens.Count > 0 && tokens[^1].Depth == 0 &&
            (IsWord(tokens[^1], "ASC") || IsWord(tokens[^1], "DESC")))
        {
            descending = IsWord(tokens[^1], "DESC");
            end = tokens[^1].Start;
            tokens.RemoveAt(tokens.Count - 1);
        }

        if (tokens.Count >= 2 && tokens[^2].Depth == 0 && IsWord(tokens[^2], "COLLATE"))
        {
            collation = UnquoteIdentifier(tokens[^1].Text);
            end = Math.Min(end, tokens[^2].Start);
            tokens.RemoveRange(tokens.Count - 2, 2);
        }

        var keyText = text[..end].Trim();
        var keyTokens = Tokenize(keyText);
        if (keyTokens.Count == 1 && (keyTokens[0].Kind is TokenKind.Word or TokenKind.Identifier ||
                                    allowSingleQuotedIdentifier && keyTokens[0].Kind == TokenKind.String))
        {
            return new IndexKeyInfo(UnquoteIdentifier(keyTokens[0].Text), ordinal, descending,
                Collation: collation);
        }

        return new IndexKeyInfo(null, ordinal, descending, keyText, collation);
    }

    private static string? ExtractParenthesizedAfter(string sql, Token keyword)
    {
        var tokens = Tokenize(sql);
        var open = tokens.FirstOrDefault(t => t.Start >= keyword.End && t.Depth == keyword.Depth && t.Text == "(");
        if (open is null || !TryFindMatchingParenthesis(sql, open.Start, out var close)) return null;
        return sql[(open.Start + 1)..close].Trim();
    }

    private static bool TryFindParenthesizedBody(string sql, out int start, out int end)
    {
        var tokens = Tokenize(sql);
        var open = tokens.FirstOrDefault(t => t.Depth == 0 && t.Text == "(");
        if (open is not null && TryFindMatchingParenthesis(sql, open.Start, out end))
        {
            start = open.Start;
            return true;
        }

        start = end = -1;
        return false;
    }

    private static bool TryFindMatchingParenthesis(string sql, int open, out int close)
    {
        foreach (var token in Tokenize(sql[open..]))
        {
            if (token.Text == ")" && token.Depth == 1)
            {
                close = open + token.Start;
                return true;
            }
        }

        close = -1;
        return false;
    }

    private static IReadOnlyList<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var start = 0;
        foreach (var token in Tokenize(text))
        {
            if (token.Text != "," || token.Depth != 0) continue;
            parts.Add(text[start..token.Start]);
            start = token.End;
        }
        parts.Add(text[start..]);
        return parts;
    }

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var depth = 0;
        for (var i = 0; i < sql.Length;)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }
            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] is not ('\r' or '\n')) i++;
                continue;
            }
            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = Math.Min(sql.Length, i + 2);
                continue;
            }

            var start = i;
            if (sql[i] == '\'')
            {
                ReadQuoted(sql, ref i, '\'', '\'');
                tokens.Add(new Token(sql[start..i], start, i, TokenKind.String, depth));
            }
            else if (sql[i] is '"' or '`')
            {
                var quote = sql[i];
                ReadQuoted(sql, ref i, quote, quote);
                tokens.Add(new Token(sql[start..i], start, i, TokenKind.Identifier, depth));
            }
            else if (sql[i] == '[')
            {
                ReadQuoted(sql, ref i, '[', ']');
                tokens.Add(new Token(sql[start..i], start, i, TokenKind.Identifier, depth));
            }
            else if (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')
            {
                i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                tokens.Add(new Token(sql[start..i], start, i, TokenKind.Word, depth));
            }
            else
            {
                var symbol = sql[i++].ToString();
                tokens.Add(new Token(symbol, start, i, TokenKind.Symbol, depth));
                if (symbol == "(") depth++;
                else if (symbol == ")") depth = Math.Max(0, depth - 1);
            }
        }
        return tokens;
    }

    private static void ReadQuoted(string sql, ref int i, char open, char close)
    {
        i++; // opening delimiter
        while (i < sql.Length)
        {
            if (sql[i] != close) { i++; continue; }
            if (i + 1 < sql.Length && sql[i + 1] == close && open != '[') { i += 2; continue; }
            i++;
            break;
        }
    }

    private static bool IsWord(Token token, string word)
        => token.Kind == TokenKind.Word && string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase);

    private static string UnquoteIdentifier(string text)
    {
        if (text.Length < 2) return text;
        return text[0] switch
        {
            '"' when text[^1] == '"' => text[1..^1].Replace("\"\"", "\""),
            '`' when text[^1] == '`' => text[1..^1].Replace("``", "`"),
            '[' when text[^1] == ']' => text[1..^1],
            '\'' when text[^1] == '\'' => text[1..^1].Replace("''", "'"),
            _ => text,
        };
    }
}
