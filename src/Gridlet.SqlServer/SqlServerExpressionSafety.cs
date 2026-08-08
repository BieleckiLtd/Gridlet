namespace Gridlet.SqlServer;

internal static class SqlServerExpressionSafety
{
    public static string RequireSingleExpression(string? expression, string kind)
    {
        var value = expression?.Trim() ?? "";
        if (value.Length == 0)
        {
            throw new GridletValidationException($"A {kind} expression is required.");
        }

        Validate(value, kind);
        return value;
    }

    private static void Validate(string value, string kind)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '\0') Reject(kind);

            if (character is '\'' or '"' or '[')
            {
                var delimiter = character == '[' ? ']' : character;
                var closed = false;
                for (i++; i < value.Length; i++)
                {
                    if (value[i] != delimiter) continue;
                    if (i + 1 < value.Length && value[i + 1] == delimiter)
                    {
                        i++;
                        continue;
                    }

                    closed = true;
                    break;
                }

                if (!closed) Reject(kind);
                continue;
            }

            if (character == ';' ||
                character == '-' && i + 1 < value.Length && value[i + 1] == '-' ||
                character == '/' && i + 1 < value.Length && value[i + 1] == '*' ||
                character == '*' && i + 1 < value.Length && value[i + 1] == '/')
            {
                Reject(kind);
            }

            if (character == '(') depth++;
            else if (character == ')' && --depth < 0) Reject(kind);
        }

        if (depth != 0) Reject(kind);
    }

    private static void Reject(string kind)
        => throw new GridletValidationException(
            $"The {kind} expression must be one balanced SQL expression without comments or statement separators.");
}
