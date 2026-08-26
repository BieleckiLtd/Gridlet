using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gridlet.AspNetCore;

/// <summary>
/// Streams a whole table/view through the provider's bounded paging API. Only one provider page
/// and one encoded row are retained at a time, regardless of the object's size.
/// </summary>
internal sealed class GridletTableExportResult(
    ITableDataService data,
    GridletConnectionContext context,
    string schema,
    string name,
    string format,
    string? sort,
    SortDirection direction,
    IReadOnlyList<TableDataFilter>? filters,
    int pageSize,
    TableDataPage firstPage,
    ILogger<GridletTableExportResult> logger) : IResult
{
    private static readonly byte[] JsonArrayStart = [(byte)'['];
    private static readonly byte[] JsonArrayEnd = [(byte)']'];
    private static readonly byte[] JsonRowSeparator = [(byte)','];

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var fileName = SafeFileName(name) + "." + format;
        var asciiFileName = string.Concat(fileName.Select(character => character <= 0x7f ? character : '_'));
        httpContext.Response.ContentType = format == "json"
            ? "application/json; charset=utf-8"
            : "text/csv; charset=utf-8";
        httpContext.Response.Headers.ContentDisposition =
            $"attachment; filename=\"{asciiFileName}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";

        try
        {
            if (format == "json")
            {
                await WriteJsonAsync(httpContext.Response.Body, cancellationToken);
            }
            else
            {
                await WriteCsvAsync(httpContext.Response.Body, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away. RequestAborted has already closed the only consumer.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Full {Format} export of {Schema}.{Name} failed after the response started.",
                format, schema, name);
            if (!httpContext.Response.HasStarted)
            {
                throw;
            }

            // CSV and JSON have no standards-compliant in-band error record. Terminate the
            // response abruptly so a browser cannot mistake a well-formed partial file for a
            // completed export.
            httpContext.Abort();
        }
    }

    private async Task WriteCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        await WriteCsvRowAsync(writer, firstPage.Columns.Select(column => column.Name), cancellationToken);

        await ForEachPageAsync(async page =>
        {
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteCsvRowAsync(
                    writer,
                    Enumerable.Range(0, firstPage.Columns.Count)
                        .Select(index => ExportText(index < row.Length ? row[index] : null)),
                    cancellationToken);
            }
            await writer.FlushAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task WriteJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(JsonArrayStart, cancellationToken);
        var firstRow = true;
        var buffer = new ArrayBufferWriter<byte>();
        await ForEachPageAsync(async page =>
        {
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!firstRow)
                {
                    await stream.WriteAsync(JsonRowSeparator, cancellationToken);
                }
                firstRow = false;

                buffer.Clear();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    for (var index = 0; index < firstPage.Columns.Count; index++)
                    {
                        writer.WritePropertyName(firstPage.Columns[index].Name);
                        JsonSerializer.Serialize(
                            writer,
                            index < row.Length ? row[index] : null,
                            JsonSerializerOptions.Web);
                    }
                    writer.WriteEndObject();
                }
                await stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
        }, cancellationToken);
        await stream.WriteAsync(JsonArrayEnd, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task ForEachPageAsync(
        Func<TableDataPage, Task> consume,
        CancellationToken cancellationToken)
    {
        var page = firstPage;
        var exported = 0L;
        // The first count is the export boundary. Inserts that race a long export cannot make the
        // response grow forever; provider paging and request cancellation remain in force.
        var targetRows = firstPage.TotalRows;
        var pageNumber = 1;
        while (true)
        {
            await consume(page);
            exported += page.Rows.Count;
            if (page.Rows.Count == 0 || exported >= targetRows)
            {
                break;
            }

            pageNumber++;
            page = await data.GetPageAsync(
                context,
                schema,
                name,
                new TableDataRequest(pageNumber, pageSize, sort, direction, filters),
                cancellationToken);
        }
    }

    private static async Task WriteCsvRowAsync(
        TextWriter writer,
        IEnumerable<string> values,
        CancellationToken cancellationToken)
    {
        var line = string.Join(',', values.Select(CsvEscape));
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    private static string CsvEscape(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string ExportText(object? value)
        => value switch
        {
            null => string.Empty,
            string text => text,
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.GetRawText(),
            },
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
        };

    private static string SafeFileName(string value)
    {
        var safe = new string(value.Select(character =>
            character < ' ' || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '_'
                : character).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(safe) ? "gridlet-export" : safe;
    }
}
