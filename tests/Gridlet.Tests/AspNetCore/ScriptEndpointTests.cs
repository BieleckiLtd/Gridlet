using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Tests.AspNetCore.Fakes;
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
        Assert.Contains("INSERT INTO dbo.Customers (Id, Name) VALUES (1, Ada);", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_row_cap_is_reported_when_it_stops_the_script()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var sql = await ScriptAsync(client, new { include = new[] { "data" }, maxRows = 1 });

        Assert.Contains("-- Stopped at 1 rows.", sql, StringComparison.Ordinal);
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
