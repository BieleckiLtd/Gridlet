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
        IReadOnlyDictionary<string, string> ColumnCollations,
        IReadOnlyList<ParsedForeignKey> ForeignKeys);

    /// <summary>
    /// One foreign key as it was written, in declaration order. Only the parts needed to match the
    /// declaration against a <c>pragma_foreign_key_list</c> row are kept; the pragma is the
    /// authority on everything else.
    /// </summary>
    internal sealed record ParsedForeignKey(
        string? Name,
        IReadOnlyList<string> Columns,
        string ReferencedTable);

    internal sealed record ParsedIndex(
        IReadOnlyList<IndexKeyInfo> Keys,
        string? Filter);

    private sealed record Token(string Text, int Start, int End, TokenKind Kind, int Depth);
    private enum TokenKind { Word, Identifier, String, Symbol }

    public static ParsedTable ParseTable(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || !TryFindParenthesizedBody(sql, out var bodyStart, out var bodyEnd))
        {
            return new ParsedTable([], [], new Dictionary<string, string>(), []);
        }

        var checks = new List<CheckConstraintInfo>();
        var uniques = new List<UniqueConstraintInfo>();
        var columnCollations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = new List<ParsedForeignKey>();
        foreach (var fragment in SplitTopLevel(sql[(bodyStart + 1)..bodyEnd]))
        {
            ParseTableFragment(fragment, checks, uniques, columnCollations, foreignKeys);
        }

        return new ParsedTable(
            checks.Select((item, ordinal) => item with { Ordinal = ordinal }).ToArray(),
            uniques.Select((item, ordinal) => item with { Ordinal = ordinal }).ToArray(),
            columnCollations,
            foreignKeys);
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
    /// <param name="foreignKeys">Receives each foreign key in the order it was written.</param>
    private static void ParseTableFragment(
        string rawFragment,
        List<CheckConstraintInfo> checks,
        List<UniqueConstraintInfo> uniques,
        Dictionary<string, string> columnCollations,
        List<ParsedForeignKey> foreignKeys)
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

        if (position + 1 < tokens.Count &&
            IsWord(tokens[position], "FOREIGN") && IsWord(tokens[position + 1], "KEY"))
        {
            var body = ExtractParenthesizedAfter(fragment, tokens[position + 1]);
            var referenced = FindReferencedTable(tokens, position + 2);
            var keyColumns = body is null ? null : ParseColumnNames(body);
            if (keyColumns is not null && referenced is not null)
            {
                foreignKeys.Add(new ParsedForeignKey(tableConstraintName, keyColumns, referenced));
            }
            return;
        }

        // PRIMARY KEY, and a FOREIGN KEY that did not parse, are table constraints rather than
        // column definitions, so neither starts with a column name.
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
            else if (IsWord(token, "REFERENCES"))
            {
                // A column-level REFERENCES is a foreign key on that one column. SQLite reports it
                // through the same pragma as a table-level FOREIGN KEY.
                var referenced = FindReferencedTable(tokens, i);
                if (referenced is not null)
                {
                    foreignKeys.Add(new ParsedForeignKey(pendingConstraintName, [columnName], referenced));
                }
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

    /// <summary>
    /// Reads a foreign key's parenthesized column list. Every entry has to be one plain identifier,
    /// which is all SQLite allows there; the list is tokenized rather than split on text so quoting
    /// and comments do not become part of a column name. Returns null when any entry is something
    /// else, which leaves the caller with no usable declaration rather than a wrong one.
    /// </summary>
    private static IReadOnlyList<string>? ParseColumnNames(string body)
    {
        var names = new List<string>();
        foreach (var part in SplitTopLevel(body))
        {
            var tokens = Tokenize(part);
            if (tokens.Count != 1 || !IsIdentifierToken(tokens[0])) return null;
            names.Add(UnquoteIdentifier(tokens[0].Text));
        }

        return names.Count == 0 ? null : names;
    }

    /// <summary>
    /// True when a token names something. A single-quoted token counts: SQLite reads one as an
    /// identifier wherever a string literal cannot appear, which includes a constraint's column
    /// list and the table after REFERENCES, and older tools wrote them that way.
    /// </summary>
    private static bool IsIdentifierToken(Token token)
        => token.Kind is TokenKind.Word or TokenKind.Identifier or TokenKind.String;

    /// <summary>
    /// Returns the table named by the first top-level REFERENCES at or after <paramref name="start"/>.
    /// </summary>
    private static string? FindReferencedTable(List<Token> tokens, int start)
    {
        for (var i = Math.Max(start, 0); i < tokens.Count; i++)
        {
            if (tokens[i].Depth != 0 || !IsWord(tokens[i], "REFERENCES")) continue;
            if (i + 1 >= tokens.Count || tokens[i + 1].Depth != 0 || !IsIdentifierToken(tokens[i + 1]))
            {
                return null;
            }

            var name = UnquoteIdentifier(tokens[i + 1].Text);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
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
            // Only ASCII whitespace separates tokens. SQLite's whitespace is space, tab, newline,
            // carriage return, form feed and vertical tab; anything at or above U+0080 belongs to
            // the identifier, even where .NET calls it whitespace (U+00A0, for one).
            if (sql[i] < '\u0080' && char.IsWhiteSpace(sql[i])) { i++; continue; }
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
            else if (IsWordCharacter(sql[i]))
            {
                i++;
                while (i < sql.Length && IsWordCharacter(sql[i])) i++;
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

    /// <summary>
    /// Characters SQLite accepts inside an unquoted identifier. SQLite treats every character at or
    /// above U+0080 as an identifier character, so a name written with a combining mark stays one
    /// token here rather than splitting into several.
    /// </summary>
    private static bool IsWordCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '$' || value >= '\u0080';

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
