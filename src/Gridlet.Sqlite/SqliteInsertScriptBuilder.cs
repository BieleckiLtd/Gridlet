using System.Globalization;
using System.Text;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>Writes rows as SQLite INSERT statements.</summary>
public static class SqliteInsertScriptBuilder
{
    /// <summary>Scripts always break lines the same way, whatever platform they were built on.</summary>
    private const string Newline = "\n";

    /// <summary>
    /// Builds the INSERT statements for <paramref name="rows"/>. Lines end with a newline whatever
    /// the host platform is, so the script reads the same wherever it is opened.
    /// </summary>
    public static string Build(
        TableDefinition table,
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<object?[]> rows)
    {
        var target = SqliteIdentifier.QuoteQualified(table.Object.Schema, table.Object.Name);
        var byName = table.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        // A generated column is derived, not stored input, and a hidden one is not addressable.
        // Everything else is scripted, including the rowid alias primary key, so the script
        // reproduces the same rows rather than similar ones.
        var writable = columns
            .Select((column, ordinal) => (Column: column, Ordinal: ordinal))
            .Where(entry => !byName.TryGetValue(entry.Column.Name, out var info)
                || (!info.IsComputed && !info.IsHidden))
            .ToArray();
        if (writable.Length == 0 || rows.Count == 0)
        {
            return $"-- No rows to script for {target}.";
        }

        var script = new StringBuilder();
        var columnList = string.Join(", ", writable.Select(entry => SqliteIdentifier.Quote(entry.Column.Name)));
        foreach (var row in rows)
        {
            var values = string.Join(", ", writable.Select(entry => Literal(row.ElementAtOrDefault(entry.Ordinal))));
            script.Append($"INSERT INTO {target} ({columnList}) VALUES ({values});").Append(Newline);
        }

        return script.ToString().TrimEnd();
    }

    private static string Literal(object? value)
        => value switch
        {
            null or DBNull => "NULL",
            bool flag => flag ? "1" : "0",
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            DateTime date => $"'{date:yyyy-MM-dd HH:mm:ss.fff}'",
            DateTimeOffset offset => $"'{offset:yyyy-MM-dd HH:mm:ss.fffzzz}'",
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => "'" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("'", "''") + "'",
        };
}
