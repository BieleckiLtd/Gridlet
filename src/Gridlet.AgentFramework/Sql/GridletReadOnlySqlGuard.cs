namespace Gridlet.AgentFramework;

/// <summary>
/// Conservative, provider-neutral defense-in-depth guard for SQL proposed by a model. Database
/// permissions remain the authoritative security boundary.
/// </summary>
public static class GridletReadOnlySqlGuard
{
    private static readonly HashSet<string> DangerousTokens = new(StringComparer.Ordinal)
    {
        "ALTER", "ANALYZE", "ATTACH", "BACKUP", "BEGIN", "BULK", "CALL", "CLUSTER",
        "COMMENT", "COMMIT", "COPY", "CREATE", "DBCC", "DEALLOCATE", "DECLARE", "DELETE",
        "DENY", "DETACH", "DISCARD", "DO", "DROP", "EXEC", "EXECUTE", "GET_LOCK", "GO",
        "GRANT", "INSERT", "INTO", "KILL", "LOAD_EXTENSION", "LOCK", "MERGE", "OPENQUERY",
        "OPENDATASOURCE", "OPENROWSET", "PRAGMA", "PREPARE", "RECONFIGURE", "REFRESH",
        "REINDEX", "RELEASE_LOCK", "RENAME", "REPLACE", "RESET", "RESTORE", "REVOKE",
        "ROLLBACK", "SAVEPOINT", "SECURITY", "SET", "SETVAL", "SHUTDOWN", "TRUNCATE",
        "UNLOCK", "UPDATE", "UPSERT", "USE", "VACUUM", "WAITFOR",
    };

    /// <summary>Throws when <paramref name="sql"/> is not one read-only SELECT/CTE statement.</summary>
    public static void Validate(string? sql)
    {
        if (!TryValidate(sql, out var error))
        {
            throw new GridletValidationException(error!);
        }
    }

    /// <summary>Checks whether SQL is one conservatively read-only SELECT/CTE statement.</summary>
    public static bool TryValidate(string? sql, out string? error)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            error = "The query tool requires a non-empty SQL statement.";
            return false;
        }
        if (sql.IndexOf('\0') >= 0)
        {
            error = "The query contains an invalid null character.";
            return false;
        }

        if (!TryTokenize(sql, out var tokens, out error))
        {
            return false;
        }
        if (tokens.Count == 0)
        {
            error = "The query tool requires a SELECT or WITH ... SELECT statement.";
            return false;
        }
        if (tokens[0] is not ("SELECT" or "WITH"))
        {
            error = "Only SELECT or WITH ... SELECT statements are allowed.";
            return false;
        }
        if (tokens[0] == "WITH" && !tokens.Contains("SELECT", StringComparer.Ordinal))
        {
            error = "A WITH statement must end in a SELECT query.";
            return false;
        }

        foreach (var token in tokens)
        {
            if (DangerousTokens.Contains(token))
            {
                error = token == "INTO"
                    ? "SELECT INTO is not allowed."
                    : $"The SQL token '{token}' is not allowed by the read-only query guard.";
                return false;
            }
        }

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index] == "NEXT" && tokens[index + 1] == "VALUE")
            {
                error = "Sequence mutation is not allowed by the read-only query guard.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryTokenize(string sql, out List<string> tokens, out string? error)
    {
        tokens = [];
        error = null;
        var index = 0;
        var terminated = false;

        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }
            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                if (!SkipBlockComment(sql, ref index))
                {
                    error = "The query contains an unterminated block comment.";
                    return false;
                }
                continue;
            }
            if (terminated)
            {
                error = "Only one SQL statement is allowed.";
                return false;
            }

            var character = sql[index];
            if (character == ';')
            {
                terminated = true;
                index++;
                continue;
            }
            if (character == '\'')
            {
                if (!SkipDelimited(sql, ref index, '\'', '\'', allowDoubledClosing: true))
                {
                    error = "The query contains an unterminated string literal.";
                    return false;
                }
                continue;
            }
            if (character == '"')
            {
                if (!SkipDelimited(sql, ref index, '"', '"', allowDoubledClosing: true))
                {
                    error = "The query contains an unterminated quoted identifier.";
                    return false;
                }
                continue;
            }
            if (character == '[')
            {
                if (!SkipDelimited(sql, ref index, '[', ']', allowDoubledClosing: true))
                {
                    error = "The query contains an unterminated bracketed identifier.";
                    return false;
                }
                continue;
            }
            if (character == '`')
            {
                if (!SkipDelimited(sql, ref index, '`', '`', allowDoubledClosing: true))
                {
                    error = "The query contains an unterminated quoted identifier.";
                    return false;
                }
                continue;
            }
            if (char.IsAsciiLetter(character) || character == '_')
            {
                var start = index++;
                while (index < sql.Length &&
                       (char.IsAsciiLetterOrDigit(sql[index]) || sql[index] is '_' or '$' or '#'))
                {
                    index++;
                }
                tokens.Add(sql[start..index].ToUpperInvariant());
                continue;
            }

            index++;
        }

        return true;
    }

    private static bool SkipBlockComment(string sql, ref int index)
    {
        var depth = 1;
        index += 2;
        while (index < sql.Length)
        {
            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                depth++;
                index += 2;
            }
            else if (index + 1 < sql.Length && sql[index] == '*' && sql[index + 1] == '/')
            {
                depth--;
                index += 2;
                if (depth == 0) return true;
            }
            else
            {
                index++;
            }
        }
        return false;
    }

    private static bool SkipDelimited(
        string sql,
        ref int index,
        char opening,
        char closing,
        bool allowDoubledClosing)
    {
        if (sql[index] != opening) return false;
        index++;
        while (index < sql.Length)
        {
            if (sql[index] != closing)
            {
                index++;
                continue;
            }
            if (allowDoubledClosing && index + 1 < sql.Length && sql[index + 1] == closing)
            {
                index += 2;
                continue;
            }
            index++;
            return true;
        }
        return false;
    }
}
