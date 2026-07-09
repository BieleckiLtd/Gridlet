namespace Gridlet.SqlServer;

/// <summary>Quotes SQL Server identifiers so user-supplied names can never break out of brackets.</summary>
public static class SqlServerIdentifier
{
    private const int MaxIdentifierLength = 128;

    /// <summary>Returns <c>[name]</c> with embedded <c>]</c> characters escaped.</summary>
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

        return "[" + name.Replace("]", "]]") + "]";
    }

    /// <summary>Returns <c>[schema].[name]</c>.</summary>
    public static string QuoteQualified(string schema, string name)
        => Quote(schema) + "." + Quote(name);
}
