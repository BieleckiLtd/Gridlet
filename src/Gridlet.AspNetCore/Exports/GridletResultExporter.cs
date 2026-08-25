using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Parquet;
using Parquet.Schema;

namespace Gridlet.AspNetCore;

/// <summary>
/// Converts an already-capped browser result into real XLSX and Parquet files. It never executes
/// SQL: posting the values back avoids replaying a query that may have side effects.
/// </summary>
internal static partial class GridletResultExporter
{
    private const int ExcelMaxColumns = 16_384;
    private const int ExcelMaxCellCharacters = 32_767;
    private const int MaxCells = 2_000_000;
    private const string SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Validate(ResultExportRequest request, int maxRows)
    {
        if (request.Columns is not { Length: > 0 })
        {
            throw new GridletValidationException("At least one result column is required for export.");
        }
        if (request.Columns.Length > ExcelMaxColumns)
        {
            throw new GridletValidationException(
                $"Result exports support at most {ExcelMaxColumns:N0} columns.");
        }
        if (request.Rows is null)
        {
            throw new GridletValidationException("Result rows are required for export.");
        }
        if (request.Rows.Length > maxRows)
        {
            throw new GridletValidationException(
                $"Result exports cannot exceed the configured {maxRows:N0}-row limit.");
        }
        if ((long)request.Columns.Length * request.Rows.Length > MaxCells)
        {
            throw new GridletValidationException(
                $"Result exports cannot exceed {MaxCells:N0} cells.");
        }

        for (var columnIndex = 0; columnIndex < request.Columns.Length; columnIndex++)
        {
            if (request.Columns[columnIndex] is null || request.Columns[columnIndex].Name is null)
            {
                throw new GridletValidationException(
                    $"Export column {columnIndex + 1} is incomplete.");
            }
            if (request.Columns[columnIndex].Name.Length > 1_024)
            {
                throw new GridletValidationException(
                    $"Export column {columnIndex + 1} has a name longer than 1,024 characters.");
            }
        }
        for (var rowIndex = 0; rowIndex < request.Rows.Length; rowIndex++)
        {
            if (request.Rows[rowIndex] is null)
            {
                throw new GridletValidationException($"Export row {rowIndex + 1} is missing.");
            }
            if (request.Rows[rowIndex].Length != request.Columns.Length)
            {
                throw new GridletValidationException(
                    $"Export row {rowIndex + 1} has {request.Rows[rowIndex].Length} values; " +
                    $"{request.Columns.Length} were expected.");
            }
        }
    }

    public static byte[] WriteExcel(ResultExportRequest request)
    {
        var columns = request.Columns!;
        var rows = request.Rows!;
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXmlEntry(archive, "[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types",
                    PackageContentTypesNamespace);
                WriteContentType(writer, "Default", "Extension", "rels",
                    "application/vnd.openxmlformats-package.relationships+xml");
                WriteContentType(writer, "Default", "Extension", "xml", "application/xml");
                WriteContentType(writer, "Override", "PartName", "/xl/workbook.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                WriteContentType(writer, "Override", "PartName", "/xl/worksheets/sheet1.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                WriteContentType(writer, "Override", "PartName", "/xl/styles.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                writer.WriteEndElement();
            });
            WriteXmlEntry(archive, "_rels/.rels", writer =>
            {
                writer.WriteStartElement("Relationships",
                    PackageRelationshipsNamespace);
                WriteRelationship(writer, "rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                    "xl/workbook.xml");
                writer.WriteEndElement();
            });
            WriteXmlEntry(archive, "xl/workbook.xml", writer =>
            {
                writer.WriteStartElement("workbook", SpreadsheetNamespace);
                writer.WriteAttributeString("xmlns", "r", null, OfficeRelationshipsNamespace);
                writer.WriteStartElement("sheets", SpreadsheetNamespace);
                writer.WriteStartElement("sheet", SpreadsheetNamespace);
                writer.WriteAttributeString("name", "Results");
                writer.WriteAttributeString("sheetId", "1");
                writer.WriteAttributeString("r", "id", OfficeRelationshipsNamespace, "rId1");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            });
            WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships",
                    PackageRelationshipsNamespace);
                WriteRelationship(writer, "rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    "worksheets/sheet1.xml");
                WriteRelationship(writer, "rId2",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
                    "styles.xml");
                writer.WriteEndElement();
            });
            WriteXmlEntry(archive, "xl/styles.xml", WriteExcelStyles);
            WriteXmlEntry(archive, "xl/worksheets/sheet1.xml", writer =>
                WriteExcelWorksheet(writer, columns, rows));
        }
        return output.ToArray();
    }

    public static async Task<byte[]> WriteParquetAsync(
        ResultExportRequest request,
        CancellationToken cancellationToken)
    {
        var columns = BuildParquetColumns(
            request.Columns!, request.Rows!, request.ProviderName ?? string.Empty);
        var schema = new ParquetSchema(columns.Select(column => column.Field));
        using var output = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(
            schema, output, cancellationToken: cancellationToken))
        {
            writer.CustomMetadata = new Dictionary<string, string>
            {
                ["gridlet.export"] = "loaded-results",
            };
            if (request.Rows!.Length > 0)
            {
                using var rowGroup = writer.CreateRowGroup();
                foreach (var column in columns)
                {
                    await column.WriteAsync(rowGroup, cancellationToken);
                }
            }
        }
        return output.ToArray();
    }

    private static IReadOnlyList<ParquetColumn> BuildParquetColumns(
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<JsonElement[]> rows,
        string providerName)
    {
        var names = UniqueColumnNames(columns);
        return columns.Select((column, index) =>
        {
            var values = rows.Select(row => row[index]).ToArray();
            var dataTypeName = column.DataTypeName ?? string.Empty;
            var kind = ParquetKindFor(providerName, dataTypeName, values);
            var field = CreateParquetField(names[index], dataTypeName, values, kind);
            return new ParquetColumn(field, kind, values, index + 1);
        }).ToArray();
    }

    private static DataField CreateParquetField(
        string name,
        string dataTypeName,
        JsonElement[] values,
        ParquetValueKind kind)
        => kind switch
        {
            ParquetValueKind.Boolean => new DataField<bool>(name, nullable: true),
            ParquetValueKind.Int32 => new DataField<int>(name, nullable: true),
            ParquetValueKind.Int64 => new DataField<long>(name, nullable: true),
            ParquetValueKind.Double => new DataField<double>(name, nullable: true),
            ParquetValueKind.Decimal => DecimalField(name, dataTypeName, values),
            ParquetValueKind.Date => new DateTimeDataField(
                name, DateTimeFormat.Date, isAdjustedToUTC: false, isNullable: true),
            ParquetValueKind.DateTime => new DateTimeDataField(
                name, DateTimeFormat.Timestamp, isAdjustedToUTC: false,
                // Nanosecond timestamps use an Int64 count whose range is only 1677-2262.
                // Microseconds cover SQL Server's full datetime/datetime2 range (and DateTime's)
                // while retaining the greatest interoperable precision available for those values.
                unit: DateTimeTimeUnit.Micros, isNullable: true),
            ParquetValueKind.Binary => new DataField<byte[]>(name, nullable: true),
            _ => new DataField<string>(name, nullable: true),
        };

    private static DecimalDataField DecimalField(
        string name,
        string dataTypeName,
        IReadOnlyList<JsonElement> values)
    {
        var match = DecimalType().Match(dataTypeName);
        if (match.Success)
        {
            var declaredPrecision = Math.Clamp(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 1, 38);
            var declaredScale = Math.Clamp(
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 0, declaredPrecision);
            return new DecimalDataField(name, declaredPrecision, declaredScale, isNullable: true);
        }
        if (dataTypeName.Contains("smallmoney", StringComparison.OrdinalIgnoreCase))
        {
            return new DecimalDataField(name, 10, 4, isNullable: true);
        }
        if (dataTypeName.Contains("money", StringComparison.OrdinalIgnoreCase))
        {
            return new DecimalDataField(name, 19, 4, isNullable: true);
        }

        var decimals = values.Where(IsValue).Select(ValueAsDecimal).ToArray();
        var scale = decimals.Length == 0 ? 0 : decimals.Max(DecimalScale);
        var precision = decimals.Length == 0 ? 29 : Math.Max(
            1,
            scale + decimals.Max(DecimalIntegerDigits));
        if (precision > 29)
        {
            throw new GridletValidationException(
                $"Decimal values in column '{name}' require precision {precision} at scale {scale}, " +
                "which exceeds the supported inferred precision of 29.");
        }
        return new DecimalDataField(name, precision, scale, isNullable: true);
    }

    private static ParquetValueKind ParquetKindFor(
        string providerName,
        string dataTypeName,
        IReadOnlyList<JsonElement> values)
    {
        var type = dataTypeName.Trim().ToLowerInvariant();
        if (type is "bit" or "bool" or "boolean") return ParquetValueKind.Boolean;
        if (type.StartsWith("tinyint", StringComparison.Ordinal)
            || type.StartsWith("smallint", StringComparison.Ordinal)
            || type == "int" || type.StartsWith("int(", StringComparison.Ordinal))
        {
            return ParquetValueKind.Int32;
        }
        if (type.StartsWith("bigint", StringComparison.Ordinal) || type == "integer")
        {
            return ParquetValueKind.Int64;
        }
        if (type.StartsWith("decimal", StringComparison.Ordinal)
            || type.StartsWith("numeric", StringComparison.Ordinal)
            || type.Contains("money", StringComparison.Ordinal))
        {
            return ParquetValueKind.Decimal;
        }
        if (type.StartsWith("real", StringComparison.Ordinal)
            || type.StartsWith("float", StringComparison.Ordinal)
            || type.StartsWith("double", StringComparison.Ordinal))
        {
            return ParquetValueKind.Double;
        }
        if (type == "date") return ParquetValueKind.Date;
        if ((type.StartsWith("datetime", StringComparison.Ordinal)
                && !type.StartsWith("datetimeoffset", StringComparison.Ordinal))
            || type.StartsWith("smalldatetime", StringComparison.Ordinal))
        {
            return ParquetValueKind.DateTime;
        }
        if (type.Contains("binary", StringComparison.Ordinal)
            || type is "image" or "rowversion" or "blob"
            || (type == "timestamp"
                && providerName.Contains("sqlserver", StringComparison.OrdinalIgnoreCase)))
        {
            return ParquetValueKind.Binary;
        }

        var actual = values.Where(IsValue).ToArray();
        if (actual.Length > 0)
        {
            if (actual.All(value => value.ValueKind is JsonValueKind.True or JsonValueKind.False))
                return ParquetValueKind.Boolean;
            if (actual.All(value => value.ValueKind == JsonValueKind.Number))
                return actual.All(value => value.TryGetInt64(out _))
                    ? ParquetValueKind.Int64
                    : ParquetValueKind.Double;
        }
        return ParquetValueKind.String;
    }

    private static string[] UniqueColumnNames(IReadOnlyList<ResultColumn> columns)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++)
        {
            var basis = string.IsNullOrWhiteSpace(columns[index].Name)
                ? $"Column{index + 1}"
                : columns[index].Name;
            var name = basis;
            var suffix = 2;
            while (!used.Add(name)) name = $"{basis}_{suffix++}";
            names[index] = name;
        }
        return names;
    }

    private static void WriteExcelWorksheet(
        XmlWriter writer,
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<JsonElement[]> rows)
    {
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetViews", SpreadsheetNamespace);
        writer.WriteStartElement("sheetView", SpreadsheetNamespace);
        writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane", SpreadsheetNamespace);
        writer.WriteAttributeString("ySplit", "1");
        writer.WriteAttributeString("topLeftCell", "A2");
        writer.WriteAttributeString("activePane", "bottomLeft");
        writer.WriteAttributeString("state", "frozen");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cols", SpreadsheetNamespace);
        for (var index = 0; index < columns.Count; index++)
        {
            var width = Math.Clamp(columns[index].Name.Length + 2, 10, 60);
            foreach (var row in rows.Take(200))
            {
                width = Math.Clamp(Math.Max(width, DisplayValue(row[index]).Length + 2), 10, 60);
            }
            writer.WriteStartElement("col", SpreadsheetNamespace);
            writer.WriteAttributeString("min", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", "1");
        for (var index = 0; index < columns.Count; index++)
        {
            WriteInlineStringCell(writer, CellReference(index, 1), columns[index].Name, style: 1);
        }
        writer.WriteEndElement();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var excelRow = rowIndex + 2;
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", excelRow.ToString(CultureInfo.InvariantCulture));
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                WriteExcelCell(
                    writer,
                    CellReference(columnIndex, excelRow),
                    columns[columnIndex],
                    rows[rowIndex][columnIndex],
                    rowIndex + 1);
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("autoFilter", SpreadsheetNamespace);
        writer.WriteAttributeString("ref", $"A1:{CellReference(columns.Count - 1, rows.Count + 1)}");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteExcelCell(
        XmlWriter writer,
        string reference,
        ResultColumn column,
        JsonElement value,
        int rowNumber)
    {
        if (!IsValue(value)) return;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", reference);
            writer.WriteAttributeString("t", "b");
            writer.WriteElementString("v", SpreadsheetNamespace,
                value.ValueKind == JsonValueKind.True ? "1" : "0");
            writer.WriteEndElement();
            return;
        }
        if (value.ValueKind == JsonValueKind.Number && ExcelNumberIsExact(value.GetRawText()))
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", reference);
            writer.WriteElementString("v", SpreadsheetNamespace, value.GetRawText());
            writer.WriteEndElement();
            return;
        }
        if (value.ValueKind == JsonValueKind.String
            && TryExcelDate(
                column.DataTypeName ?? string.Empty,
                value.GetString(),
                out var date,
                out var dateOnly))
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", reference);
            writer.WriteAttributeString("s", dateOnly ? "2" : "3");
            writer.WriteElementString("v", SpreadsheetNamespace,
                date.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
            return;
        }

        var text = DisplayValue(value);
        if (text.Length > ExcelMaxCellCharacters)
        {
            throw new GridletValidationException(
                $"Excel cannot store the value in column '{column.Name}', row {rowNumber}: " +
                $"it exceeds {ExcelMaxCellCharacters:N0} characters.");
        }
        WriteInlineStringCell(writer, reference, text, style: null);
    }

    private static bool TryExcelDate(
        string dataTypeName,
        string? text,
        out DateTime value,
        out bool dateOnly)
    {
        var type = dataTypeName.Trim().ToLowerInvariant();
        dateOnly = type == "date";
        var supported = dateOnly || (type.StartsWith("datetime", StringComparison.Ordinal)
                && !type.StartsWith("datetimeoffset", StringComparison.Ordinal))
            || type.StartsWith("smalldatetime", StringComparison.Ordinal);
        if (!supported || !DateTime.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
        {
            value = default;
            return false;
        }
        // Excel and OLE Automation disagree for January and February 1900 because Excel preserves
        // the historical, fictional 1900-02-29. To avoid silently shifting those dates by a day,
        // keep them as ISO text. From 1900-03-01 onward their serials agree.
        if (value < new DateTime(1900, 3, 1) || value.Year > 9999)
        {
            value = default;
            return false;
        }
        return true;
    }

    private static bool ExcelNumberIsExact(string raw)
    {
        var mantissa = raw.Split('e', 'E')[0].TrimStart('-', '+').Replace(".", "", StringComparison.Ordinal);
        return mantissa.TrimStart('0').Length <= 15;
    }

    private static void WriteInlineStringCell(
        XmlWriter writer,
        string reference,
        string value,
        int? style)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("t", "inlineStr");
        if (style is not null)
        {
            writer.WriteAttributeString("s", style.Value.ToString(CultureInfo.InvariantCulture));
        }
        writer.WriteStartElement("is", SpreadsheetNamespace);
        writer.WriteStartElement("t", SpreadsheetNamespace);
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(SanitizeXml(value));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteExcelStyles(XmlWriter writer)
    {
        writer.WriteStartElement("styleSheet", SpreadsheetNamespace);
        writer.WriteStartElement("numFmts", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("numFmt", SpreadsheetNamespace);
        writer.WriteAttributeString("numFmtId", "164");
        writer.WriteAttributeString("formatCode", "yyyy-mm-dd hh:mm:ss.000");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("fonts", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "2");
        WriteFont(writer, bold: false);
        WriteFont(writer, bold: true);
        writer.WriteEndElement();
        writer.WriteStartElement("fills", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "2");
        WriteFill(writer, "none");
        WriteFill(writer, "gray125");
        writer.WriteEndElement();
        writer.WriteStartElement("borders", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border", SpreadsheetNamespace);
        foreach (var edge in new[] { "left", "right", "top", "bottom", "diagonal" })
        {
            writer.WriteStartElement(edge, SpreadsheetNamespace);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        WriteXf(writer, 0, 0);
        writer.WriteEndElement();
        writer.WriteStartElement("cellXfs", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "4");
        WriteXf(writer, 0, 0);
        WriteXf(writer, 1, 0, applyFont: true);
        WriteXf(writer, 0, 14, applyNumberFormat: true);
        WriteXf(writer, 0, 164, applyNumberFormat: true);
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyles", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle", SpreadsheetNamespace);
        writer.WriteAttributeString("name", "Normal");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("builtinId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFont(XmlWriter writer, bool bold)
    {
        writer.WriteStartElement("font", SpreadsheetNamespace);
        if (bold)
        {
            writer.WriteStartElement("b", SpreadsheetNamespace);
            writer.WriteEndElement();
        }
        writer.WriteStartElement("sz", SpreadsheetNamespace);
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("name", SpreadsheetNamespace);
        writer.WriteAttributeString("val", "Aptos");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string patternType)
    {
        writer.WriteStartElement("fill", SpreadsheetNamespace);
        writer.WriteStartElement("patternFill", SpreadsheetNamespace);
        writer.WriteAttributeString("patternType", patternType);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteXf(
        XmlWriter writer,
        int fontId,
        int numberFormatId,
        bool applyFont = false,
        bool applyNumberFormat = false)
    {
        writer.WriteStartElement("xf", SpreadsheetNamespace);
        writer.WriteAttributeString("numFmtId", numberFormatId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        if (applyFont) writer.WriteAttributeString("applyFont", "1");
        if (applyNumberFormat) writer.WriteAttributeString("applyNumberFormat", "1");
        writer.WriteEndElement();
    }

    private static void WriteContentType(
        XmlWriter writer,
        string element,
        string key,
        string value,
        string contentType)
    {
        writer.WriteStartElement(element, PackageContentTypesNamespace);
        writer.WriteAttributeString(key, value);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRelationship(
        XmlWriter writer,
        string id,
        string type,
        string target)
    {
        writer.WriteStartElement("Relationship", PackageRelationshipsNamespace);
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    private static void WriteXmlEntry(
        ZipArchive archive,
        string name,
        Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        });
        write(writer);
    }

    private static string CellReference(int zeroBasedColumn, int row)
    {
        var column = zeroBasedColumn + 1;
        Span<char> letters = stackalloc char[3];
        var position = letters.Length;
        while (column > 0)
        {
            column--;
            letters[--position] = (char)('A' + column % 26);
            column /= 26;
        }
        return $"{letters[position..].ToString()}{row.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string DisplayValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };

    private static string SanitizeXml(string value)
    {
        var sanitized = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                sanitized.Append(character).Append(value[++index]);
            }
            else if (char.IsSurrogate(character) || !XmlConvert.IsXmlChar(character))
            {
                sanitized.Append('\uFFFD');
            }
            else
            {
                sanitized.Append(character);
            }
        }
        return sanitized.ToString();
    }

    private static bool IsValue(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static decimal ValueAsDecimal(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed)
            ? parsed
            : throw new GridletValidationException(
                "A decimal export value is not a JSON number or is outside the supported range.");

    private static int DecimalScale(decimal value)
        => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static int DecimalIntegerDigits(decimal value)
    {
        var integer = decimal.Truncate(Math.Abs(value));
        if (integer == 0) return 0;
        return integer.ToString("0", CultureInfo.InvariantCulture).Length;
    }

    [GeneratedRegex(@"(?:decimal|numeric)\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex DecimalType();

    private enum ParquetValueKind
    {
        String,
        Boolean,
        Int32,
        Int64,
        Double,
        Decimal,
        Date,
        DateTime,
        Binary,
    }

    private sealed record ParquetColumn(
        DataField Field,
        ParquetValueKind Kind,
        JsonElement[] Values,
        int ColumnNumber)
    {
        public Task WriteAsync(
            ParquetRowGroupWriter writer,
            CancellationToken cancellationToken)
            => Kind switch
            {
                ParquetValueKind.Boolean => writer.WriteAsync<bool>(
                    Field, ConvertNullable<bool>(value => value.GetBoolean()).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Int32 => writer.WriteAsync<int>(
                    Field, ConvertNullable<int>(value => value.GetInt32()).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Int64 => writer.WriteAsync<long>(
                    Field, ConvertNullable<long>(value => value.GetInt64()).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Double => writer.WriteAsync<double>(
                    Field, ConvertNullable<double>(value => value.GetDouble()).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Decimal => writer.WriteAsync<decimal>(
                    Field, ConvertNullable<decimal>(ValueAsDecimal).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Date or ParquetValueKind.DateTime => writer.WriteAsync<DateTime>(
                    Field, ConvertNullable<DateTime>(ParseDateTime).AsMemory(),
                    cancellationToken: cancellationToken),
                ParquetValueKind.Binary => writer.WriteAsync(Field, ConvertBinary()),
                _ => writer.WriteAsync(Field,
                    Values.Select(value => IsValue(value) ? DisplayValue(value) : null).ToArray()),
            };

        private T?[] ConvertNullable<T>(Func<JsonElement, T> convert)
            where T : struct
        {
            var converted = new T?[Values.Length];
            for (var index = 0; index < Values.Length; index++)
            {
                if (!IsValue(Values[index])) continue;
                try
                {
                    converted[index] = convert(Values[index]);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException
                    or OverflowException)
                {
                    throw InvalidValue(index);
                }
            }
            return converted;
        }

        private byte[]?[] ConvertBinary()
        {
            var converted = new byte[]?[Values.Length];
            for (var index = 0; index < Values.Length; index++)
            {
                if (!IsValue(Values[index])) continue;
                try
                {
                    converted[index] = Values[index].ValueKind == JsonValueKind.String
                        ? Convert.FromBase64String(Values[index].GetString() ?? string.Empty)
                        : throw new FormatException("Binary values must be base64 strings.");
                }
                catch (FormatException)
                {
                    throw InvalidValue(index);
                }
            }
            return converted;
        }

        private DateTime ParseDateTime(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(
                    value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date))
            {
                return date;
            }
            throw new FormatException("Date values must be ISO-8601 strings.");
        }

        private GridletValidationException InvalidValue(int zeroBasedRow)
            => new(
                $"Export value in column {ColumnNumber}, row {zeroBasedRow + 1} " +
                "does not match the column data type.");
    }
}
