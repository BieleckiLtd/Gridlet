using System.Text;
using System.Text.Json;
using Gridlet.Models;

namespace Gridlet.AspNetCore;

internal static class GridletImportParser
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 1_024;
    public const long MaxCells = 2_000_000;
    public const long MaxBytes = 10 * 1024 * 1024;
    public const long MaxRequestBytes = MaxBytes + (64 * 1024);

    public static TableImport Parse(
        string content, string format, IReadOnlyDictionary<string, string>? mapping = null)
    {
        var source = format.ToLowerInvariant() switch
        {
            "csv" => ParseCsv(content),
            "json" => ParseJson(content),
            _ => throw new GridletValidationException("Import format must be 'csv' or 'json'."),
        };
        return ApplyMapping(source, mapping);
    }

    private static TableImport ApplyMapping(
        TableImport source, IReadOnlyDictionary<string, string>? mapping)
    {
        if (mapping is null || mapping.Count == 0) return source;
        var sourceOrdinals = source.Columns
            .Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var selected = new List<(int Ordinal, string Target)>();
        foreach (var pair in mapping)
        {
            if (!sourceOrdinals.TryGetValue(pair.Key, out var ordinal))
                throw new GridletValidationException($"Import source column '{pair.Key}' does not exist.");
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            selected.Add((ordinal, pair.Value.Trim()));
        }
        if (selected.Count == 0) throw new GridletValidationException("The import mapping does not select any columns.");
        return new TableImport(
            selected.Select(item => item.Target).ToArray(),
            source.Rows.Select(row => (IReadOnlyList<object?>)selected.Select(item => row[item.Ordinal]).ToArray()).ToArray());
    }

    private static TableImport ParseJson(string content)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content); }
        catch (JsonException ex) { throw new GridletValidationException($"The JSON import is invalid: {ex.Message}"); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new GridletValidationException("A JSON import must be an array of objects.");
            if (document.RootElement.GetArrayLength() > MaxRows)
                throw new GridletValidationException($"An import may contain at most {MaxRows:N0} rows.");

            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new GridletValidationException("Every JSON import row must be an object.");
                foreach (var property in item.EnumerateObject())
                {
                    if (!seen.Add(property.Name)) continue;
                    columns.Add(property.Name);
                    if (columns.Count > MaxColumns)
                        throw new GridletValidationException($"An import may contain at most {MaxColumns:N0} columns.");
                }
            }
            if (columns.Count == 0) throw new GridletValidationException("The JSON import has no columns.");
            if ((long)document.RootElement.GetArrayLength() * columns.Count > MaxCells)
                throw new GridletValidationException($"An import may contain at most {MaxCells:N0} values.");

            var rows = document.RootElement.EnumerateArray().Select(item =>
            {
                var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    if (!values.TryAdd(property.Name, property.Value))
                        throw new GridletValidationException(
                            $"JSON import row contains duplicate column '{property.Name}'.");
                }
                return (IReadOnlyList<object?>)columns.Select(column =>
                    values.TryGetValue(column, out var value) ? ToClr(value) : null).ToArray();
            }).ToArray();
            return new TableImport(columns, rows);
        }
    }

    private static object? ToClr(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        _ => value.GetRawText(),
    };

    private static TableImport ParseCsv(string content)
    {
        var records = ReadCsvRecords(content);
        if (records.Count == 0) throw new GridletValidationException("The CSV import is empty.");
        var headers = records[0].Select(header => header.Value.Trim()).ToArray();
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace))
            throw new GridletValidationException("Every CSV column needs a header.");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new GridletValidationException("CSV headers must be unique.");
        if (headers.Length > MaxColumns)
            throw new GridletValidationException($"An import may contain at most {MaxColumns:N0} columns.");
        if (records.Count - 1 > MaxRows)
            throw new GridletValidationException($"An import may contain at most {MaxRows:N0} rows.");

        var rows = new List<IReadOnlyList<object?>>(Math.Max(0, records.Count - 1));
        foreach (var record in records.Skip(1))
        {
            if (record.Count != headers.Length)
                throw new GridletValidationException(
                    $"CSV record {rows.Count + 2} has {record.Count} values; {headers.Length} were expected.");
            rows.Add(record.Select(field =>
                field.WasQuoted || field.Value.Length > 0 ? (object?)field.Value : null).ToArray());
        }
        return new TableImport(headers, rows);
    }

    private static List<List<CsvField>> ReadCsvRecords(string content)
    {
        var records = new List<List<CsvField>>();
        var record = new List<CsvField>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldWasQuoted = false;
        var recordStarted = false;
        long cellCount = 0;
        for (var index = 0; index <= content.Length; index++)
        {
            var character = index < content.Length ? content[index] : '\n';
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else quoted = false;
                }
                else field.Append(character);
                continue;
            }
            if (character == '"' && field.Length == 0)
            {
                quoted = true;
                fieldWasQuoted = true;
                recordStarted = true;
                continue;
            }
            if (character == ',')
            {
                record.Add(new CsvField(field.ToString(), fieldWasQuoted));
                if (record.Count > MaxColumns)
                    throw new GridletValidationException($"An import may contain at most {MaxColumns:N0} columns.");
                field.Clear();
                fieldWasQuoted = false;
                recordStarted = true;
                continue;
            }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                record.Add(new CsvField(field.ToString(), fieldWasQuoted));
                if (record.Count > MaxColumns)
                    throw new GridletValidationException($"An import may contain at most {MaxColumns:N0} columns.");
                field.Clear();
                if (recordStarted || records.Count == 0)
                {
                    records.Add(record);
                    cellCount += record.Count;
                    if (records.Count - 1 > MaxRows)
                        throw new GridletValidationException($"An import may contain at most {MaxRows:N0} rows.");
                    if (cellCount > MaxCells + MaxColumns)
                        throw new GridletValidationException($"An import may contain at most {MaxCells:N0} values.");
                }
                record = [];
                fieldWasQuoted = false;
                recordStarted = false;
                continue;
            }
            field.Append(character);
            recordStarted = true;
        }
        if (quoted) throw new GridletValidationException("The CSV import contains an unterminated quoted field.");
        return records;
    }

    private readonly record struct CsvField(string Value, bool WasQuoted);
}
