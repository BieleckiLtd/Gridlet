using Gridlet.Models;

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

        // SQLite accepts square-bracket quoting for compatibility, but unlike SQL Server it has
        // no escape sequence for a closing bracket inside an identifier. Double quotes are the
        // native SQLite form and escape embedded quotes by doubling them.
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string QuoteQualified(string schema, string name)
    {
        return Quote(schema) + "." + Quote(name);
    }

    public static string SelectedSchema(GridletConnectionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Database) ||
            context.Database.Equals(MainSchema, StringComparison.OrdinalIgnoreCase))
            return MainSchema;
        return context.Connection.SqliteAttachments.Keys.FirstOrDefault(key =>
                   key.Equals(context.Database, StringComparison.OrdinalIgnoreCase))
               ?? context.Database;
    }

    public static void RequireSelectedSchema(GridletConnectionContext context, string schema)
    {
        var selected = SelectedSchema(context);
        if (!string.Equals(schema, selected, StringComparison.Ordinal))
        {
            throw new GridletValidationException(
                $"SQLite database '{selected}' does not contain schema '{schema}'.");
        }
    }

    public static void RequireDatabaseName(string schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new GridletValidationException("A SQLite database name is required.");
        }
    }
}
