using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gridlet.Abstractions;
using Gridlet.AspNetCore;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class ImportEndpointTests
{
    private const string Url =
        "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/import";

    [Fact]
    public async Task Import_endpoint_rejects_oversized_requests_before_form_buffering()
    {
        var (app, _) = await GridletTestHost.StartDefaultAsync();
        await using var cleanup = app;
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText?.EndsWith("/import") == true);

        Assert.Equal(GridletImportParser.MaxRequestBytes,
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(GridletImportParser.MaxBytes,
            endpoint.Metadata.GetMetadata<RequestFormLimitsAttribute>()?.MultipartBodyLengthLimit);
    }

    [Fact]
    public async Task Csv_upload_applies_column_mapping_and_reaches_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "Full Name,Years\r\n\"Lovelace, Ada\",36\r\nGrace,40\r\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "people.csv");
        content.Add(new StringContent("{\"Full Name\":\"FirstName\",\"Years\":\"Age\"}"), "mapping");
        content.Headers.Add("X-Gridlet-Request", "1");

        var response = await client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        Assert.Equal(["FirstName", "Age"], fake.LastImport!.Columns);
        Assert.Equal("Lovelace, Ada", fake.LastImport.Rows[0][0]);
        Assert.Contains("import dbo.Customers 2", fake.Calls);
    }

    [Fact]
    public async Task Import_rejects_a_cross_site_compatible_form_without_the_required_header()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Name\nAda\n"), "file", "people.csv");

        var response = await client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Gridlet-Request", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Json_upload_parses_heterogeneous_objects_and_nulls()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(
            "[{\"Name\":\"Ada\",\"Age\":36},{\"name\":\"Grace\",\"Active\":true}]"),
            "file", "people.json");
        content.Headers.Add("X-Gridlet-Request", "1");

        var response = await client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var import = ((FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>()).LastImport!;
        Assert.Equal(["Name", "Age", "Active"], import.Columns);
        Assert.Equal(["Ada", 36L, null], import.Rows[0]);
        Assert.Equal(["Grace", null, true], import.Rows[1]);
    }

    [Fact]
    public async Task Json_upload_is_rejected_when_writes_are_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name,
                connection => connection.AllowWrites = false);
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("[{\"FirstName\":\"Ada\"}]"), "file", "people.json");

        var response = await client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
