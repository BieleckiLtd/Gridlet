using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
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
    ILogger<GridletTableExportResult> logger,
    int maximumPageCount = 100_000) : IResult
{
    private static readonly byte[] JsonArrayStart = [(byte)'['];
    private static readonly byte[] JsonArrayEnd = [(byte)']'];
    private static readonly byte[] JsonRowSeparator = [(byte)','];
    private readonly string[] columnNames = UniqueColumnNames(firstPage.Columns);

    /// <summary>
    /// Exercises the format-specific conversion of the bounded page that the endpoint already
    /// fetched. This happens before either a probe response or attachment headers are committed,
    /// so an unsupported provider value is reported as a normal, redacted API error.
    /// </summary>
    internal void ValidateFirstPage(CancellationToken cancellationToken)
    {
        if (firstPage.Rows.Any(row => row.Length != firstPage.Columns.Count))
        {
            throw new GridletValidationException(
                "The data provider returned a row whose values do not match the export columns.");
        }
        if (firstPage.RowKeys is { } rowKeys && rowKeys.Count != firstPage.Rows.Count)
        {
            throw new GridletValidationException(
                "The data provider returned incomplete row identities during this export.");
        }

        if (format == "json")
        {
            var buffer = new ArrayBufferWriter<byte>();
            foreach (var row in firstPage.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SerializeJsonRow(buffer, row);
            }
            return;
        }

        foreach (var columnName in columnNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = CsvEscape(SpreadsheetSafeText(columnName));
        }
        foreach (var row in firstPage.Rows)
        {
            for (var index = 0; index < firstPage.Columns.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = CsvEscape(ExportCsvText(index < row.Length ? row[index] : null));
            }
        }
    }

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
        httpContext.Response.Headers.CacheControl = "no-store";

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
        catch (Exception ex) when (
            (httpContext.Response.HasStarted && ex is IOException or ObjectDisposedException)
            || (cancellationToken.IsCancellationRequested
                && ex is OperationCanceledException or IOException or ObjectDisposedException))
        {
            // The client went away. RequestAborted has already closed the only consumer. Include
            // StreamWriter flush/dispose faults which can replace the original cancellation.
        }
        catch (Exception ex)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.Clear();
                httpContext.Response.Headers.CacheControl = "no-store";
                httpContext.Response.Headers.XContentTypeOptions = "nosniff";
                var statusCode = ex switch
                {
                    GridletObjectNotFoundException => StatusCodes.Status404NotFound,
                    GridletValidationException or GridletQueryException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError,
                };
                var clientMessage = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected server error occurred."
                    : ex.Message;
                if (statusCode == StatusCodes.Status500InternalServerError)
                {
                    logger.LogError(ex,
                        "Full {Format} export of {Schema}.{Name} failed before the response started.",
                        format, schema, name);
                }
                httpContext.Response.StatusCode = statusCode;
                await httpContext.Response.WriteAsJsonAsync(
                    new GridletErrorResponse(clientMessage), cancellationToken);
                return;
            }

            logger.LogError(ex,
                "Full {Format} export of {Schema}.{Name} failed after the response started.",
                format, schema, name);
            // CSV and JSON have no standards-compliant in-band error record. Terminate the
            // response abruptly so a browser cannot mistake a well-formed partial file for a
            // completed export.
            httpContext.Abort();
        }
    }

    private async Task WriteCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        var writer = new StreamWriter(
            stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        var completed = false;
        try
        {
            await WriteCsvRowAsync(
                writer, columnNames.Select(SpreadsheetSafeText), cancellationToken);
            await ForEachPageAsync(async page =>
            {
                foreach (var row in page.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteCsvRowAsync(
                        writer,
                        Enumerable.Range(0, firstPage.Columns.Count)
                            .Select(index => ExportCsvText(index < row.Length ? row[index] : null)),
                        cancellationToken);
                }
                await writer.FlushAsync(cancellationToken);
            }, cancellationToken);
            completed = true;
        }
        finally
        {
            // Disposing flushes. Only do that after success, otherwise it can commit attachment
            // headers while an early failure is unwinding and prevent a structured error response.
            if (completed) await writer.DisposeAsync();
        }
    }

    private async Task WriteJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        await stream.WriteAsync(JsonArrayStart, cancellationToken);
        var firstRow = true;
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

                SerializeJsonRow(buffer, row);
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
        var pageNumber = 1;
        var exported = 0L;
        string? previousBoundary = null;
        while (true)
        {
            if (page.Page != pageNumber || page.PageSize <= 0 || page.TotalRows < 0
                || page.Rows.Count > page.PageSize
                || !page.Columns.SequenceEqual(firstPage.Columns)
                || page.Rows.Any(row => row.Length != firstPage.Columns.Count))
            {
                throw new GridletValidationException(
                    "The data provider returned invalid paging metadata during this export.");
            }
            if (page.RowKeys is { } rowKeys)
            {
                if (rowKeys.Count != page.Rows.Count)
                {
                    throw new GridletValidationException(
                        "The data provider returned incomplete row identities during this export.");
                }
                if (rowKeys.Count > 0)
                {
                    var boundary = JsonSerializer.Serialize(
                        new[] { rowKeys[0], rowKeys[^1] }, JsonSerializerOptions.Web);
                    if (string.Equals(boundary, previousBoundary, StringComparison.Ordinal))
                    {
                        throw new GridletValidationException(
                            "The data provider repeated a page without making export progress.");
                    }
                    previousBoundary = boundary;
                }
            }
            await consume(page);
            exported += page.Rows.Count;
            // Providers may clamp the requested page size. Use the size they report, and consult
            // TotalRows only to distinguish an actual final short page from a clamped/partial page.
            // An empty page is always terminal, including when an overestimated count is stale.
            if (page.Rows.Count == 0
                || (page.Rows.Count < Math.Max(1, page.PageSize) && exported >= page.TotalRows))
            {
                break;
            }

            if (pageNumber >= maximumPageCount)
            {
                throw new GridletValidationException(
                    $"The export exceeded its safety limit of {maximumPageCount:N0} provider pages.");
            }

            pageNumber++;
            try
            {
                page = await data.GetPageAsync(
                    context,
                    schema,
                    name,
                    new TableDataRequest(pageNumber, pageSize, sort, direction, filters),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Keep provider I/O faults distinct from response-body I/O. The outer handler
                // must log and abort a live download rather than mistaking a database failure for
                // a client disconnect and returning a cleanly truncated file.
                throw new GridletQueryException(
                    "The data provider failed while reading an export page.", ex);
            }
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

    private static string ExportCsvText(object? value)
    {
        var text = ExportText(value);
        return value is string or JsonElement { ValueKind: JsonValueKind.String }
            ? SpreadsheetSafeText(text)
            : text;
    }

    private static string SpreadsheetSafeText(string value)
    {
        foreach (var character in value)
        {
            if (character is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                return "'" + value;
            }
            if (!char.IsWhiteSpace(character))
            {
                break;
            }
        }
        return value;
    }

    private void SerializeJsonRow(ArrayBufferWriter<byte> buffer, object?[] row)
    {
        buffer.Clear();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        for (var index = 0; index < firstPage.Columns.Count; index++)
        {
            writer.WritePropertyName(columnNames[index]);
            JsonSerializer.Serialize(
                writer,
                index < row.Length ? row[index] : null,
                JsonSerializerOptions.Web);
        }
        writer.WriteEndObject();
    }

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

    private static string[] UniqueColumnNames(IReadOnlyList<ResultColumn> columns)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++)
        {
            var basis = string.IsNullOrWhiteSpace(columns[index].Name)
                ? $"Column{index + 1}"
                : columns[index].Name;
            var name = basis;
            var suffix = 2;
            while (!used.Add(name))
            {
                name = $"{basis}_{suffix++}";
            }
            names[index] = name;
        }
        return names;
    }
}
