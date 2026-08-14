using System.Globalization;
using System.Text;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>
/// Writes rows as INSERT statements. This is the escape hatch when the designer will not do
/// something: a script can be edited, reviewed and run somewhere else, which is exactly what people
/// reach for an external tool to get.
/// </summary>
public static class SqlServerInsertScriptBuilder
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
        var target = SqlServerIdentifier.QuoteQualified(table.Object.Schema, table.Object.Name);
        var byName = table.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var currentTemporal = table.Temporal is { Kind: TemporalTableKinds.SystemVersioned } value
            ? value
            : null;
        var periodColumns = new HashSet<string>(
            new[] { currentTemporal?.PeriodStartColumn, currentTemporal?.PeriodEndColumn }.OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        // Computed columns cannot be written at all; identity columns can, but only inside
        // IDENTITY_INSERT, which is why the script turns it on rather than dropping the column.
        var writable = columns
            .Select((column, ordinal) => (Column: column, Ordinal: ordinal))
            .Where(entry => !byName.TryGetValue(entry.Column.Name, out var info)
                || (!info.IsComputed && !info.IsHidden && !periodColumns.Contains(info.Name)))
            .ToArray();
        if (writable.Length == 0 || rows.Count == 0)
        {
            return $"-- No rows to script for {target}.";
        }

        var hasIdentity = writable.Any(entry =>
            byName.TryGetValue(entry.Column.Name, out var info) && info.IsIdentity);
        var script = new StringBuilder();
        if (hasIdentity)
        {
            script.Append($"SET IDENTITY_INSERT {target} ON;").Append(Newline);
        }

        var columnList = string.Join(", ", writable.Select(entry => SqlServerIdentifier.Quote(entry.Column.Name)));
        foreach (var row in rows)
        {
            var values = string.Join(", ", writable.Select(entry =>
                Literal(row.ElementAtOrDefault(entry.Ordinal), byName.GetValueOrDefault(entry.Column.Name))));
            script.Append($"INSERT INTO {target} ({columnList}) VALUES ({values});").Append(Newline);
        }

        if (hasIdentity)
        {
            script.Append($"SET IDENTITY_INSERT {target} OFF;").Append(Newline);
        }

        return script.ToString().TrimEnd();
    }

    /// <summary>Renders one value as a T-SQL literal of its column's type.</summary>
    private static string Literal(object? value, ColumnInfo? column)
    {
        if (value is null or DBNull)
        {
            return "NULL";
        }

        var baseType = (column?.DataType ?? "").Split('(')[0].Trim().ToLowerInvariant();
        return value switch
        {
            bool flag => flag ? "1" : "0",
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            Guid guid => $"'{guid:D}'",
            DateTime date => $"'{date:yyyy-MM-ddTHH:mm:ss.fffffff}'",
            DateTimeOffset offset => $"'{offset:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'",
            DateOnly date => $"'{date:yyyy-MM-dd}'",
            TimeOnly time => $"'{time:HH:mm:ss.fffffff}'",
            TimeSpan span => $"'{span:c}'",
            sbyte or byte or short or ushort or int or uint or long or ulong
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => QuotedText(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", baseType),
        };
    }

    private static string QuotedText(string value, string baseType)
    {
        var escaped = value.Replace("'", "''");
        return baseType is "char" or "varchar" or "text" ? $"'{escaped}'" : $"N'{escaped}'";
    }
}
