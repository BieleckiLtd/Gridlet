using System.Net;
using System.Net.Http.Json;
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
    public async Task Administration_endpoints_expose_security_and_manage_all_trigger_scopes()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        const string database = "/gridlet/api/connections/Main/databases/FakeDb";

        var security = await client.GetStringAsync($"{database}/security");
        var triggers = await client.GetStringAsync($"{database}/triggers");
        var changed = await client.PostAsJsonAsync($"{database}/triggers/state", new
        {
            name = "AuditDatabaseDdl",
            scope = "database",
            enabled = true,
        });

        Assert.Contains("\"currentUser\":\"app_user\"", security);
        Assert.Contains("\"role\":\"report_reader\"", security);
        Assert.Contains("\"permission\":\"SELECT\"", security);
        Assert.Contains("\"name\":\"AuditDatabaseDdl\"", triggers);
        Assert.Contains("\"scope\":\"server\"", triggers);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        Assert.Contains("setTriggerState database..AuditDatabaseDdl enabled=True", fake.Calls);
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
