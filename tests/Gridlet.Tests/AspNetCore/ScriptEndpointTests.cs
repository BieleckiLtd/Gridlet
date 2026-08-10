using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class ScriptEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Customers/script";

    private static async Task<string> ScriptAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Base, body);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sql").GetString()!;
    }

    [Fact]
    public async Task Create_is_the_default()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client, new { });

        Assert.StartsWith("CREATE VIEW dbo.Customers", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP", sql, StringComparison.Ordinal);
    }

    /// <summary>The parts have to come back in an order that runs top to bottom.</summary>
    [Fact]
    public async Task Drop_create_and_data_are_scripted_in_the_order_they_run()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client, new { include = new[] { "data", "create", "drop" } });

        var drop = sql.IndexOf("DROP TABLE", StringComparison.Ordinal);
        var create = sql.IndexOf("CREATE VIEW", StringComparison.Ordinal);
        var insert = sql.IndexOf("INSERT INTO", StringComparison.Ordinal);
        Assert.True(drop >= 0 && create > drop && insert > create, sql);

        // How the fake writes a value is its own business; this test is about the order of the parts.
        Assert.Contains("INSERT INTO dbo.Customers (Id, Name) VALUES (", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_row_cap_is_reported_when_it_stops_the_script()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client, new { include = new[] { "data" }, maxRows = 1 });

        Assert.Contains("-- Stopped at 1 rows.", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Paging only holds together while every page comes from the same order, and the providers get
    /// that order from the row identity. A table that has none is read in one request instead: two
    /// pages of an unordered table can repeat rows from the first and skip others, which would
    /// script rows the table never held in that combination.
    /// </summary>
    [Fact]
    public async Task A_table_that_cannot_be_ordered_is_read_in_one_request()
    {
        var (app, client, fake) = await PagedHostAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client,
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/LedgerHeap/script",
            new { include = new[] { "data" }, maxRows = 10 });

        var request = Assert.Single(fake.DataPageRequests);
        Assert.Equal((1, 10), request);
        Assert.Equal(4, sql.Split("INSERT INTO", StringSplitOptions.None).Length - 1);
    }

    /// <summary>A table that can be ordered still pages, so the fix costs nothing where it is safe.</summary>
    [Fact]
    public async Task A_table_that_can_be_ordered_is_still_read_a_page_at_a_time()
    {
        var (app, client, fake) = await PagedHostAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client,
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Ledger/script",
            new { include = new[] { "data" }, maxRows = 10 });

        Assert.Equal([(1, 2), (2, 2)], fake.DataPageRequests);
        Assert.Equal(4, sql.Split("INSERT INTO", StringSplitOptions.None).Length - 1);
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client,
        FakeGridletProvider Fake)> PagedHostAsync()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
            options.Limits.DefaultPageSize = 2;
            options.Limits.MaxPageSize = 2;
        });
        var fake = (FakeGridletProvider)app.Services
            .GetRequiredService<Gridlet.Abstractions.IGridletProvider>();
        return (app, client, fake);
    }

    private static async Task<string> ScriptAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sql").GetString()!;
    }

    /// <summary>
    /// Dropping or creating needs the object's identity, not a description of its columns, so
    /// scripting a procedure must not depend on the provider describing one as a table. The fake
    /// refuses to, as a strict provider would.
    /// </summary>
    [Fact]
    public async Task A_routine_is_scripted_without_asking_for_a_table_definition()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/RefreshOrders/script",
            new { include = new[] { "drop", "create" } });

        response.EnsureSuccessStatusCode();
        var sql = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sql").GetString()!;
        Assert.Contains("DROP STOREDPROCEDURE dbo.RefreshOrders;", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE VIEW dbo.RefreshOrders", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_routine_has_no_rows_to_script()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/RefreshOrders/script",
            new { include = new[] { "data" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no rows to script", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_object_that_does_not_exist_is_reported_as_missing()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Nope/script", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_part_is_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Base, new { include = new[] { "everything" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scripting_nothing_is_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Base, new { include = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
