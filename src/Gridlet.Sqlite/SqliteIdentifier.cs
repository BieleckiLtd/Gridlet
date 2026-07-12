namespace Gridlet.Sqlite;

/// <summary>Quotes and validates SQLite identifiers.</summary>
public static class SqliteIdentifier
{
    public const string MainSchema = "main";
    private const int MaxIdentifierLength = 255;

    public static string Quote(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new GridletValidationException("Identifier must not be empty.");
        }

        if (name.Length > MaxIdentifierLength)
        {
            throw new GridletValidationException(
                $"Identifier '{name[..32]}...' exceeds the maximum length of {MaxIdentifierLength} characters.");
        }

        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string QuoteQualified(string schema, string name)
    {
        RequireMainSchema(schema);
        return Quote(MainSchema) + "." + Quote(name);
    }

    public static void RequireMainSchema(string schema)
    {
        if (!string.Equals(schema, MainSchema, StringComparison.OrdinalIgnoreCase))
        {
            throw new GridletValidationException(
                $"SQLite exposes its primary database as schema '{MainSchema}'; schema '{schema}' is not supported.");
        }
    }
}
