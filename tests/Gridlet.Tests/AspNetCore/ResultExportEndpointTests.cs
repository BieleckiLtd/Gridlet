using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.AspNetCore;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Parquet;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class ResultExportEndpointTests
{
    private static readonly object Request = new
    {
        columns = new[]
        {
            new { name = "Id", dataTypeName = "int" },
            new { name = "Formula", dataTypeName = "nvarchar(max)" },
            new { name = "Active", dataTypeName = "bit" },
            new { name = "Missing", dataTypeName = "int" },
            new { name = "When", dataTypeName = "datetime2" },
            new { name = "Amount", dataTypeName = "decimal(18,2)" },
            new { name = "Payload", dataTypeName = "varbinary(max)" },
            new { name = "Offset", dataTypeName = "datetimeoffset" },
            new { name = "Id", dataTypeName = "bigint" },
        },
        rows = new object?[][]
        {
            [42, "=SUM(A1:A2) 😀", true, null, "2026-08-24T05:06:07.1234567", 12.34m, "AP8=",
                "2026-08-24T05:06:07+02:00", 9_000_000_000L],
            [-7, "plain", false, 9, null, null, null, null, -9_000_000_000L],
        },
        providerName = "SqlServer",
    };

    [Fact]
    public async Task Excel_export_is_a_safe_typed_ooxml_workbook()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", Request);
        var content = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("gridlet-results.xlsx",
            response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(content, 0, 2));
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
        var contentTypes = System.Xml.Linq.XDocument.Parse(
            await ReadEntryAsync(archive, "[Content_Types].xml"));
        Assert.All(contentTypes.Root!.Elements(), element => Assert.Equal(
            "http://schemas.openxmlformats.org/package/2006/content-types",
            element.Name.NamespaceName));
        var worksheet = await ReadEntryAsync(archive, "xl/worksheets/sheet1.xml");
        var worksheetXml = System.Xml.Linq.XDocument.Parse(worksheet);
        Assert.All(worksheetXml.Root!.Descendants(), element => Assert.Equal(
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
            element.Name.NamespaceName));
        Assert.Contains("<pane ySplit=\"1\"", worksheet, StringComparison.Ordinal);
        Assert.Contains("<autoFilter ref=\"A1:I3\"", worksheet, StringComparison.Ordinal);
        Assert.Contains("=SUM(A1:A2) 😀", worksheet, StringComparison.Ordinal);
        Assert.DoesNotContain("<f", worksheet, StringComparison.Ordinal);
        Assert.Contains("r=\"C2\" t=\"b\"><v>1</v>", worksheet, StringComparison.Ordinal);
        Assert.Contains("r=\"E2\" s=\"3\"><v>", worksheet, StringComparison.Ordinal);
        Assert.Contains("r=\"F2\"><v>12.34</v>", worksheet, StringComparison.Ordinal);
        Assert.DoesNotContain("r=\"D2\"", worksheet, StringComparison.Ordinal);
        Assert.Contains("2026-08-24T05:06:07+02:00", worksheet, StringComparison.Ordinal);

        var earlyDateResponse = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", new
        {
            columns = new[] { new { name = "Early", dataTypeName = "date" } },
            rows = new object?[][] { ["1900-01-01"], ["1900-03-01"] },
            providerName = "SqlServer",
        });
        using var earlyDateArchive = new ZipArchive(
            new MemoryStream(await earlyDateResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        var earlyDateWorksheet = await ReadEntryAsync(earlyDateArchive, "xl/worksheets/sheet1.xml");
        Assert.Contains("r=\"A2\" t=\"inlineStr\"", earlyDateWorksheet, StringComparison.Ordinal);
        Assert.Contains("1900-01-01", earlyDateWorksheet, StringComparison.Ordinal);
        Assert.Contains("r=\"A3\" s=\"2\"><v>61</v>", earlyDateWorksheet, StringComparison.Ordinal);

        var sqliteTimestampResponse = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", new
        {
            columns = new[] { new { name = "Created", dataTypeName = "TIMESTAMP" } },
            rows = new object?[][] { ["2026-08-24T05:06:07"] },
            providerName = "SQLite",
        });
        using var sqliteTimestampArchive = new ZipArchive(
            new MemoryStream(await sqliteTimestampResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        var sqliteTimestampWorksheet = await ReadEntryAsync(
            sqliteTimestampArchive, "xl/worksheets/sheet1.xml");
        Assert.Contains("r=\"A2\" s=\"3\"><v>", sqliteTimestampWorksheet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parquet_export_preserves_schema_values_and_nulls()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", Request);
        var content = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.apache.parquet", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("gridlet-results.parquet",
            response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("PAR1", System.Text.Encoding.ASCII.GetString(content, 0, 4));
        Assert.Equal("PAR1", System.Text.Encoding.ASCII.GetString(content, content.Length - 4, 4));
        await using var reader = await ParquetReader.CreateAsync(new MemoryStream(content));
        Assert.Equal(1, reader.RowGroupCount);
        Assert.Equal(
            ["Id", "Formula", "Active", "Missing", "When", "Amount", "Payload", "Offset", "Id_2"],
            reader.Schema.DataFields.Select(field => field.Name));
        using var rowGroup = reader.OpenRowGroupReader(0);
        Assert.Equal(2, rowGroup.RowCount);

        var ids = new int?[2];
        await rowGroup.ReadAsync<int>(reader.Schema.DataFields[0], ids.AsMemory());
        Assert.Equal([42, -7], ids);
        var formulas = new string?[2];
        await rowGroup.ReadAsync(reader.Schema.DataFields[1], formulas.AsMemory());
        Assert.Equal(new string?[] { "=SUM(A1:A2) 😀", "plain" }, formulas);
        var active = new bool?[2];
        await rowGroup.ReadAsync<bool>(reader.Schema.DataFields[2], active.AsMemory());
        Assert.Equal([true, false], active);
        var missing = new int?[2];
        await rowGroup.ReadAsync<int>(reader.Schema.DataFields[3], missing.AsMemory());
        Assert.Equal([null, 9], missing);
        var when = new DateTime?[2];
        await rowGroup.ReadAsync<DateTime>(reader.Schema.DataFields[4], when.AsMemory());
        Assert.Equal(new DateTime(2026, 8, 24, 5, 6, 7, 123).AddTicks(4_560), when[0]);
        Assert.Null(when[1]);
        var amounts = new decimal?[2];
        await rowGroup.ReadAsync<decimal>(reader.Schema.DataFields[5], amounts.AsMemory());
        Assert.Equal(12.34m, amounts[0]);
        Assert.Null(amounts[1]);
        var payloads = new byte[]?[2];
        await rowGroup.ReadAsync(reader.Schema.DataFields[6], payloads.AsMemory());
        Assert.Equal([0, 255], payloads[0]);
        Assert.Null(payloads[1]);
        var offsets = new string?[2];
        await rowGroup.ReadAsync(reader.Schema.DataFields[7], offsets.AsMemory());
        Assert.Equal(new string?[] { "2026-08-24T05:06:07+02:00", null }, offsets);
        var duplicateIds = new long?[2];
        await rowGroup.ReadAsync<long>(reader.Schema.DataFields[8], duplicateIds.AsMemory());
        Assert.Equal([9_000_000_000L, -9_000_000_000L], duplicateIds);

        var emptyResponse = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "Id", dataTypeName = "int" } },
            rows = Array.Empty<object?[]>(),
            providerName = "SqlServer",
        });
        await using var emptyReader = await ParquetReader.CreateAsync(
            new MemoryStream(await emptyResponse.Content.ReadAsByteArrayAsync()));
        Assert.Equal(0, emptyReader.RowGroupCount);
        Assert.Equal("Id", Assert.Single(emptyReader.Schema.DataFields).Name);

        var inferredResponse = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[]
            {
                new { name = "Amount", dataTypeName = "decimal" },
                new { name = "When", dataTypeName = "datetime2" },
            },
            rows = new object?[][]
            {
                [123.45m, "0001-01-01T00:00:00"],
                [0.00001m, "9999-12-31T23:59:59.999999"],
            },
            providerName = "SqlServer",
        });
        Assert.Equal(HttpStatusCode.OK, inferredResponse.StatusCode);
        await using var inferredReader = await ParquetReader.CreateAsync(
            new MemoryStream(await inferredResponse.Content.ReadAsByteArrayAsync()));
        using var inferredRowGroup = inferredReader.OpenRowGroupReader(0);
        var inferredDecimals = new decimal?[2];
        await inferredRowGroup.ReadAsync<decimal>(
            inferredReader.Schema.DataFields[0], inferredDecimals.AsMemory());
        Assert.Equal(new decimal?[] { 123.45m, 0.00001m }, inferredDecimals);
        var inferredDates = new DateTime?[2];
        await inferredRowGroup.ReadAsync<DateTime>(
            inferredReader.Schema.DataFields[1], inferredDates.AsMemory());
        Assert.Equal(new DateTime(1, 1, 1), inferredDates[0]);
        Assert.Equal(new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9_999_990), inferredDates[1]);

        var dateResponse = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "BusinessDate", dataTypeName = "date" } },
            rows = new object?[][] { ["2026-08-24"], [null] },
            providerName = "SqlServer",
        });
        Assert.Equal(HttpStatusCode.OK, dateResponse.StatusCode);
        await using var dateReader = await ParquetReader.CreateAsync(
            new MemoryStream(await dateResponse.Content.ReadAsByteArrayAsync()));
        using var dateRowGroup = dateReader.OpenRowGroupReader(0);
        var dates = new DateTime?[2];
        await dateRowGroup.ReadAsync<DateTime>(
            Assert.Single(dateReader.Schema.DataFields), dates.AsMemory());
        Assert.Equal(new DateTime(2026, 8, 24), dates[0]);
        Assert.Null(dates[1]);

        var sqliteTimestampResponse = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "Created", dataTypeName = "TIMESTAMP" } },
            rows = new object?[][] { ["2026-08-24T05:06:07.123456"] },
            providerName = "SQLite",
        });
        Assert.Equal(HttpStatusCode.OK, sqliteTimestampResponse.StatusCode);
        await using var sqliteTimestampReader = await ParquetReader.CreateAsync(
            new MemoryStream(await sqliteTimestampResponse.Content.ReadAsByteArrayAsync()));
        using var sqliteTimestampRowGroup = sqliteTimestampReader.OpenRowGroupReader(0);
        var sqliteTimestamps = new DateTime?[1];
        await sqliteTimestampRowGroup.ReadAsync<DateTime>(
            Assert.Single(sqliteTimestampReader.Schema.DataFields), sqliteTimestamps.AsMemory());
        Assert.Equal(new DateTime(2026, 8, 24, 5, 6, 7).AddTicks(1_234_560), sqliteTimestamps[0]);
    }

    [Fact]
    public async Task Result_export_endpoint_has_an_explicit_request_size_limit()
    {
        var (app, _) = await GridletTestHost.StartDefaultAsync();
        await using var cleanup = app;
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText?.EndsWith("/exports/{format}") == true);

        Assert.Equal(GridletResultExporter.MaxRequestBytes,
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }

    [Fact]
    public void Excel_export_enforces_the_native_row_limit_and_honours_cancellation()
    {
        var row = new[] { JsonSerializer.SerializeToElement(1) };
        var oversized = new ResultExportRequest(
            [new ResultColumn("A", "int")],
            Enumerable.Repeat(row, GridletResultExporter.ExcelMaxDataRows + 1).ToArray(),
            "SqlServer");

        var rowLimit = Assert.Throws<GridletValidationException>(() =>
            GridletResultExporter.Validate(oversized, int.MaxValue, "xlsx"));
        Assert.Contains("1,048,575", rowLimit.Message, StringComparison.Ordinal);

        var bounded = new ResultExportRequest(
            [new ResultColumn("A", "int")], [row], "SqlServer");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            GridletResultExporter.WriteExcel(bounded, cancellation.Token));
    }

    [Fact]
    public async Task Result_exports_reject_unknown_formats_and_unbounded_or_malformed_data()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", Fakes.FakeGridletProvider.Name);
            options.Limits.MaxQueryResultRows = 1;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var unknown = await client.PostAsJsonAsync("/gridlet/api/exports/csv", Request);
        var comparisonSized = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", Request);
        var tooMany = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", new
        {
            columns = new[] { new { name = "A", dataTypeName = "int" } },
            rows = new object?[][] { [1], [2], [3] },
        });
        var malformed = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "int" } },
            rows = new object?[][] { [1, 2] },
        });
        var wrongType = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "int" } },
            rows = new object?[][] { ["not an integer"] },
        });
        var wrongDecimal = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "decimal" } },
            rows = new object?[][] { ["not a decimal"] },
        });
        var unsafeInteger = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "bigint" } },
            rows = new object?[][] { [9_007_199_254_740_992L] },
        });
        var unsafeDecimal = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", new
        {
            columns = new[] { new { name = "A", dataTypeName = "decimal(18, 1)" } },
            rows = new object?[][] { [123_456_789_012_345.6m] },
        });
        var declaredDecimalMismatch = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "decimal(3, 0)" } },
            rows = new object?[][] { [123.45m] },
        });
        var malformedBinary = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A", dataTypeName = "varbinary(max)" } },
            rows = new object?[][] { ["not-base64!!"] },
        });
        var oversizedExcelCell = await client.PostAsJsonAsync("/gridlet/api/exports/xlsx", new
        {
            columns = new[] { new { name = "A", dataTypeName = "nvarchar(max)" } },
            rows = new object?[][] { [new string('x', 32_768)] },
        });
        var inferredType = await client.PostAsJsonAsync("/gridlet/api/exports/parquet", new
        {
            columns = new[] { new { name = "A" } },
            rows = new object?[][] { ["text"] },
        });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("xlsx", await unknown.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, comparisonSized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("2-row limit", await tooMany.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("1 were expected", await malformed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Contains("does not match", await wrongType.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, wrongDecimal.StatusCode);
        Assert.Contains("decimal export value", await wrongDecimal.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeInteger.StatusCode);
        Assert.Contains("browser number precision", await unsafeInteger.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeDecimal.StatusCode);
        Assert.Contains("browser number precision", await unsafeDecimal.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, declaredDecimalMismatch.StatusCode);
        Assert.Contains("does not match", await declaredDecimalMismatch.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, malformedBinary.StatusCode);
        Assert.Contains("does not match", await malformedBinary.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedExcelCell.StatusCode);
        Assert.Contains("32,767", await oversizedExcelCell.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, inferredType.StatusCode);
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        await using var stream = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
