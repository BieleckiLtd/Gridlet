using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class GridletEndpointTests
{
    [Fact]
    public async Task Ui_index_is_served_with_mount_path_as_base_href()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/gridlet/\"", html);
        Assert.DoesNotContain("%GRIDLET_BASE%", html);
    }

    [Fact]
    public async Task Ui_respects_a_custom_mount_path()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            o =>
            {
                o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                o.Security.AllowAnonymous = true;
            },
            pattern: "/internal/db-admin");
        await using var _ = app;

        var html = await client.GetStringAsync("/internal/db-admin");

        Assert.Contains("<base href=\"/internal/db-admin/\"", html);
    }

    [Fact]
    public async Task Static_assets_are_served_from_embedded_resources()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var css = await client.GetAsync("/gridlet/assets/app.css");
        var js = await client.GetAsync("/gridlet/assets/app.js");
        var missing = await client.GetAsync("/gridlet/assets/nope.css");

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.StartsWith("text/css", css.Content.Headers.ContentType!.ToString());
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Meta_lists_connections_but_never_connection_strings()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("Main", body);
        Assert.Contains(FakeGridletProvider.Name.ToString(), body);
        Assert.DoesNotContain("secret-host", body);
        Assert.Contains("\"maxQueryResultRows\":10000", body);
        Assert.Contains("\"defaultSchema\":\"dbo\"", body);
        Assert.Contains("\"supportsStoredProcedures\":true", body);
        Assert.Contains("\"supportsTriggers\":true", body);
        Assert.Contains("\"supportsCheckConstraints\":true", body);
        Assert.Contains("\"supportsUniqueConstraints\":true", body);
        Assert.Contains("\"supportsIndexes\":true", body);
        Assert.Contains("\"supportsImport\":true", body);
    }

    [Fact]
    public async Task Object_and_column_descriptions_are_exposed_by_the_api()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var objects = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects");
        var structure = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/structure");

        Assert.Contains("People who buy from the store", objects);
        Assert.Contains("People who buy from the store", structure);
        Assert.Contains("Customer display name", structure);
    }

    [Fact]
    public async Task User_defined_type_definition_is_selected_explicitly()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var objects = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects");
        var definition = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/AccountNumber/definition?type=UserDefinedType");

        Assert.Contains("\"type\":\"UserDefinedType\"", objects);
        Assert.Contains("\"subKind\":\"alias\"", objects);
        Assert.Contains("CREATE TYPE [dbo].[AccountNumber] FROM nvarchar(32) NOT NULL;", definition);
    }

    [Fact]
    public async Task Meta_defaults_portable_ddl_capabilities_off_for_legacy_providers()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletProvider>(new MetadataFreeProvider()));
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"supportsCheckConstraints\":false", body);
        Assert.Contains("\"supportsUniqueConstraints\":false", body);
        Assert.Contains("\"supportsIndexes\":false", body);
    }

    [Fact]
    public async Task Meta_exposes_the_developer_configured_query_safety_cap()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            o.Limits.MaxQueryResultRows = 12_345;
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"maxQueryResultRows\":12345", body);
    }

    [Fact]
    public async Task Meta_exposes_the_connection_default_database()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;Database=Reporting;", FakeGridletProvider.Name,
                connection => connection.DefaultDatabase = "Reporting");
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"defaultDatabase\":\"Reporting\"", body);
        Assert.DoesNotContain("Server=x", body);
    }

    [Fact]
    public async Task Databases_come_from_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/connections/Main/databases");

        Assert.Contains("FakeDb", body);
    }

    [Fact]
    public async Task Unknown_connection_returns_404_with_error_body()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet/api/connections/Nope/databases");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Objects_expose_type_names_as_strings()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/connections/Main/databases/FakeDb/objects");

        Assert.Contains("\"Table\"", body);
        Assert.Contains("\"View\"", body);
        Assert.Contains("\"Trigger\"", body);
        Assert.Contains("\"subKind\":\"virtual\"", body);
        Assert.Contains("\"isInternal\":true", body);
    }

    [Fact]
    public async Task Structure_exposes_hidden_and_rich_constraint_and_index_metadata()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/structure");

        Assert.Contains("\"isHidden\":true", body);
        Assert.Contains("\"checkConstraints\"", body);
        Assert.Contains("CK_Customers_Name", body);
        Assert.Contains("\"uniqueConstraints\"", body);
        Assert.Contains("UQ_Customers_Name", body);
        Assert.Contains("Latin1_General_CI_AS", body);
        Assert.Contains("\"isDescending\":true", body);
        Assert.Contains("\"includedColumns\":[\"Id\"]", body);
        Assert.Contains("\"filterDefinition\":\"[Name] IS NOT NULL\"", body);
    }

    [Fact]
    public async Task Structure_exposes_how_a_row_is_addressed()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var keyed = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/structure");
        var unkeyed = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/NoKeys/structure");

        Assert.Contains("\"rowIdentity\":{\"kind\":\"primaryKey\",\"columns\":[\"Id\"]", keyed);
        Assert.Contains("\"rowIdentity\":null", unkeyed);
    }

    [Fact]
    public async Task Structure_exposes_temporal_table_relationship_and_period()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Ledger/structure");
        var history = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/LedgerHeap/structure");

        Assert.Contains("\"kind\":\"systemVersioned\"", body);
        Assert.Contains("\"relatedSchema\":\"dbo\"", body);
        Assert.Contains("\"relatedTable\":\"LedgerHeap\"", body);
        Assert.Contains("\"periodStartColumn\":\"SysStart\"", body);
        Assert.Contains("\"periodEndColumn\":\"SysEnd\"", body);
        Assert.Contains("\"historyRetentionPeriod\":6", body);
        Assert.Contains("\"historyRetentionUnit\":\"MONTH\"", body);
        Assert.Contains("\"kind\":\"historyTable\"", history);
        Assert.Contains("\"relatedSchema\":\"dbo\"", history);
        Assert.Contains("\"relatedTable\":\"Ledger\"", history);
    }

    [Fact]
    public void Previous_table_structure_response_constructor_shape_remains_available()
    {
        var constructor = typeof(TableStructureResponse).GetConstructor(
        [
            typeof(DbObjectDto), typeof(IReadOnlyList<ColumnInfo>), typeof(IReadOnlyList<IndexInfo>),
            typeof(IReadOnlyList<ForeignKeyInfo>), typeof(IReadOnlyList<CheckConstraintInfo>),
            typeof(IReadOnlyList<UniqueConstraintInfo>), typeof(RowIdentityInfo),
            typeof(IReadOnlyList<string>), typeof(IReadOnlyList<ForeignKeyDisplayDto>),
        ]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public void Pre_default_constraint_table_structure_response_constructor_remains_available()
    {
        var constructor = typeof(TableStructureResponse).GetConstructor(
        [
            typeof(DbObjectDto), typeof(IReadOnlyList<ColumnInfo>), typeof(IReadOnlyList<IndexInfo>),
            typeof(IReadOnlyList<ForeignKeyInfo>), typeof(IReadOnlyList<CheckConstraintInfo>),
            typeof(IReadOnlyList<UniqueConstraintInfo>), typeof(RowIdentityInfo),
            typeof(IReadOnlyList<string>), typeof(IReadOnlyList<ForeignKeyDisplayDto>),
            typeof(TemporalTableInfo),
        ]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public async Task Data_endpoint_returns_a_page()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data?page=1&pageSize=50");

        Assert.Contains("totalRows", body);
    }

    [Fact]
    public async Task Data_stream_returns_progressive_events()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/stream?maxRows=100");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"type\":\"resultSet\"", body);
        Assert.Contains("\"type\":\"rows\"", body);
        Assert.Contains("\"type\":\"completed\"", body);
    }

    [Fact]
    public async Task Csv_export_streams_every_row_beyond_the_interactive_cap_in_provider_pages()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
            options.Limits.MaxQueryResultRows = 1;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Ledger/data/export?format=csv");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Ledger.csv", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal(5, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith("Id,Name,SysStart,SysEnd\r\n", csv);
        Assert.Contains("1,Ada,2026-01-01T00:00:00.0000000", csv);
        Assert.Contains("4,Alan,2026-01-04T00:00:00.0000000", csv);
        Assert.Equal([(1, 2), (2, 2), (3, 2)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Json_export_preserves_values_and_the_current_sort_and_filter_scope()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        var filter = Uri.EscapeDataString(
            """[{"column":"Name","operator":"contains","value":"ad a"}]""");

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/export"
            + $"?format=json&sort=Name&dir=desc&filter={filter}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal(1, document.RootElement[0].GetProperty("Id").GetInt32());
        Assert.Equal("Ada", document.RootElement[0].GetProperty("Name").GetString());
        Assert.Equal("Name", fake.LastDataRequest!.SortColumn);
        Assert.Equal(SortDirection.Descending, fake.LastDataRequest.SortDirection);
        var applied = Assert.Single(fake.LastDataRequest.Filters!);
        Assert.Equal("Name", applied.Column);
        Assert.Equal(FilterOperator.Contains, applied.Operator);
        Assert.Equal("ad a", applied.Value);
    }

    [Fact]
    public async Task Export_rejects_an_unknown_format_before_reading_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/export?format=xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("csv or json", await response.Content.ReadAsStringAsync());
        Assert.Empty(fake.DataPageRequests);
    }

    [Fact]
    public async Task Export_probe_validates_one_memory_bounded_page_without_streaming_the_table()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.MaxPageSize = 900;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Ledger/data/export"
            + "?format=csv&probe=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"valid\":true", await response.Content.ReadAsStringAsync());
        Assert.Equal([(1, 500)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Full_export_rejects_multi_page_objects_without_a_stable_row_identity()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/LedgerHeap/data/export?format=csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("stable row identity", await response.Content.ReadAsStringAsync());
        Assert.Equal([(1, 2)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Full_export_rejects_a_clamped_underreported_heap_before_streaming()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/"
            + "ClampedUnderreportedLedgerHeap/data/export?format=json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Contains("stable row identity", await response.Content.ReadAsStringAsync());
        Assert.Equal([(1, 500)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Full_export_normalizes_a_blank_sort_and_aborts_on_a_later_page_failure()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        fake.FailDataPage = 2;

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get,
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Ledger/data/export?format=csv&sort="),
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(fake.LastDataRequest!.SortColumn);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await response.Content.ReadAsByteArrayAsync());
        Assert.Equal([(1, 2), (2, 2)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Export_does_not_trust_an_underreported_total_row_count()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var csv = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/UnderreportedLedger/data/export"
            + "?format=csv");

        Assert.Contains("4,Alan", csv);
        Assert.Equal([(1, 2), (2, 2), (3, 2)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Export_continues_when_a_provider_clamps_its_page_size()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var csv = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/ClampedLedger/data/export"
            + "?format=csv");

        Assert.Contains("4,Alan", csv);
        Assert.Equal([(1, 500), (2, 500), (3, 500)], fake.DataPageRequests);

        fake.DataPageRequests.Clear();
        var json = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/ClampedLedger/data/export"
            + "?format=json");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(4, document.RootElement.GetArrayLength());
        Assert.Equal("Alan", document.RootElement[3].GetProperty("Name").GetString());
        Assert.Equal([(1, 500), (2, 500), (3, 500)], fake.DataPageRequests);
    }

    [Fact]
    public async Task Export_aborts_when_a_provider_repeats_page_metadata_and_rows()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get,
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/RepeatingLedger/data/export"
            + "?format=csv"), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await response.Content.ReadAsByteArrayAsync());
        Assert.Equal([(1, 2), (2, 2)], fake.DataPageRequests);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("json")]
    public async Task Export_returns_a_redacted_structured_error_before_streaming_starts(string format)
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/UnserializableExport/data/export"
            + $"?format={format}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unexpected server error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cycle", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Streaming_exports_preserve_csv_escaping_and_json_value_types()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        const string export =
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/ExportCases/data/export";

        var csv = await client.GetStringAsync(export + "?format=csv");
        var json = await client.GetStringAsync(export + "?format=json");
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement[0];

        Assert.Equal(
            "Text,Binary,When,Nullable,Formula\r\n"
            + "\"a,\"\"b\r\nc\",AP8=,2026-01-02T03:04:05.0000000Z,,'=2+3\r\n",
            csv);
        Assert.Equal("a,\"b\r\nc", row.GetProperty("Text").GetString());
        Assert.Equal("AP8=", row.GetProperty("Binary").GetString());
        Assert.Equal(DateTimeKind.Utc, row.GetProperty("When").GetDateTime().Kind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("Nullable").ValueKind);
        Assert.Equal("=2+3", row.GetProperty("Formula").GetString());

        var duplicateResponse = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/DuplicateColumns/data/export?format=");
        Assert.Equal("text/csv", duplicateResponse.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("Value,value_2\r\n", await duplicateResponse.Content.ReadAsStringAsync());

        var duplicateJson = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/DuplicateColumns/data/export?format=json");
        using var duplicateDocument = JsonDocument.Parse(duplicateJson);
        Assert.Equal(1, duplicateDocument.RootElement[0].GetProperty("Value").GetInt32());
        Assert.Equal(2, duplicateDocument.RootElement[0].GetProperty("value_2").GetInt32());
    }

    [Fact]
    public async Task Data_requests_carry_filters_to_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        var filter = Uri.EscapeDataString(
            """[{"column":"Name","operator":"contains","value":"ad a"},{"column":"Notes","operator":"isNull"}]""");

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data?filter=" + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Collection(fake.LastDataFilters!,
            first =>
            {
                Assert.Equal("Name", first.Column);
                Assert.Equal(FilterOperator.Contains, first.Operator);
                Assert.Equal("ad a", first.Value);
            },
            second =>
            {
                Assert.Equal("Notes", second.Column);
                Assert.Equal(FilterOperator.IsNull, second.Operator);
                Assert.Null(second.Value);
            });
    }

    [Fact]
    public async Task Column_profile_returns_exact_statistics_and_bounds_its_request()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        var filter = Uri.EscapeDataString(
            """[{"column":"Name","operator":"contains","value":"ada"}]""");

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/profile"
            + $"?column=Status&topValues=999&filter={filter}");
        var payload = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize<ColumnProfile>(
            payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        response.EnsureSuccessStatusCode();
        Assert.NotNull(profile);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal(JsonValueKind.String,
            document.RootElement.GetProperty("topValues")[0].GetProperty("count").ValueKind);
        Assert.Equal("Status", profile.Column);
        Assert.Equal(1, profile.TotalCount);
        Assert.Equal(0, profile.NullCount);
        Assert.Equal(1, profile.DistinctCount);
        Assert.Equal(1, Assert.IsType<JsonElement>(profile.Minimum).GetInt32());
        Assert.Equal(1, Assert.IsType<JsonElement>(profile.Maximum).GetInt32());
        Assert.Collection(profile.TopValues,
            value =>
            {
                Assert.Equal(1, Assert.IsType<JsonElement>(value.Value).GetInt32());
                Assert.Equal(1, value.Count);
            });
        Assert.Contains("profile dbo.Customers.Status top(50) filters(1)", fake.Calls);
    }

    [Fact]
    public async Task Column_profile_rejects_a_missing_or_oversized_column_name()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var missing = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/profile");
        var oversized = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/profile?column="
            + new string('a', 129));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("profile column is required", await missing.Content.ReadAsStringAsync());
        Assert.Contains("profile column name is too long", await oversized.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unreadable_filter_is_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var badOperator = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data?filter="
            + Uri.EscapeDataString("""[{"column":"Name","operator":"drop"}]"""));
        var notJson = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data?filter=%7Bnope");

        Assert.Equal(HttpStatusCode.BadRequest, badOperator.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
    }

    [Fact]
    public async Task Data_stream_carries_the_row_identity_and_a_key_per_row()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/stream?maxRows=100");
        var events = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var resultSet = Assert.Single(events, line => line.Contains("\"type\":\"resultSet\""));
        Assert.Contains("\"rowIdentity\":{\"kind\":\"primaryKey\",\"columns\":[\"Id\"]", resultSet);
        var rows = Assert.Single(events, line => line.Contains("\"type\":\"rows\""));
        Assert.Contains("\"rowKeys\":[[1],[2]]", rows);
    }

    [Fact]
    public async Task Data_stream_maps_unknown_connections_before_starting_the_response()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync(
            "/gridlet/api/connections/Nope/databases/FakeDb/objects/dbo/Customers/data/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Data_stream_does_not_expose_unexpected_resolver_errors()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletConnectionResolver, SecretThrowingResolver>());
        await using var _ = app;

        var response = await client.GetAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/data/stream");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("unexpected server error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretThrowingResolver.Secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_executes_and_returns_result_sets()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "SELECT 42" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"started\"", body);
        Assert.Contains("\"type\":\"rows\"", body);
        Assert.Contains("\"type\":\"completed\"", body);
        Assert.Contains("42", body);
        Assert.Contains("hello from fake", body);
    }

    [Fact]
    public async Task Failing_query_returns_400_with_database_error()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "boom" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kaboom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_stream_maps_unknown_connections_before_starting_the_response()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Nope/databases/FakeDb/query",
            new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_stream_does_not_expose_unexpected_resolver_errors()
    {
        var (app, client) = await GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => services.AddSingleton<IGridletConnectionResolver, SecretThrowingResolver>());
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "SELECT 1" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("unexpected server error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretThrowingResolver.Secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_row_cap_cannot_exceed_the_developer_configured_maximum()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            o.Limits.MaxQueryResultRows = 250;
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/query",
            new { sql = "SELECT 42", maxRows = 50_000 });

        var provider = Assert.IsType<FakeGridletProvider>(app.Services.GetRequiredService<IGridletProvider>());
        Assert.Equal(250, provider.LastQueryOptions!.MaxRowsPerResultSet);
    }

    [Fact]
    public async Task Query_is_forbidden_when_sql_execution_is_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Locked", "Server=x;", FakeGridletProvider.Name,
                c => c.AllowSqlExecution = false);
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Locked/databases/FakeDb/query",
            new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class SecretThrowingResolver : IGridletConnectionResolver
    {
        public const string Secret = "SECRET_RESOLVER_SENTINEL";

        public ResolvedConnection Resolve(string connectionName, string? database = null)
            => throw new InvalidOperationException(Secret);
    }

    private sealed class MetadataFreeProvider : IGridletProvider
    {
        private readonly FakeGridletProvider inner = new();

        public GridletProviderNames ProviderName => FakeGridletProvider.Name;
        public ISchemaReader Schema => inner.Schema;
        public ITableDataService Data => inner.Data;
        public IQueryRunner Query => inner.Query;
        public ITableWriteService Writes => inner.Writes;
        public ITableDdlService Ddl => inner.Ddl;
    }
}
