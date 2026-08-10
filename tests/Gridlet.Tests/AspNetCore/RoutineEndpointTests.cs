using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class RoutineEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo";

    [Fact]
    public async Task A_routines_parameters_are_described()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync($"{Base}/RefreshOrders/routine");

        Assert.Contains("\"name\":\"@Since\"", body);
        Assert.Contains("\"dataType\":\"datetime2(7)\"", body);
        Assert.Contains("\"isOutput\":true", body);
        Assert.Contains("\"isReturnValue\":true", body);
    }

    [Fact]
    public async Task Arguments_are_scripted_by_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/RefreshOrders/routine/script", new
        {
            arguments = new Dictionary<string, object?>
            {
                ["@Since"] = new { value = "2026-01-01" },
                ["@RowsChanged"] = new { isNull = true },
            },
        });

        response.EnsureSuccessStatusCode();
        var sql = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sql").GetString();
        Assert.Equal("EXEC dbo.RefreshOrders @RowsChanged = NULL, @Since = 2026-01-01;", sql);
    }

    [Fact]
    public async Task An_argument_that_is_not_a_parameter_is_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/RefreshOrders/routine/script", new
        {
            arguments = new Dictionary<string, object?> { ["@Nope"] = new { value = "1" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("@Nope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_object_that_is_not_a_routine_has_no_parameters()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync($"{Base}/Customers/routine");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scripting_a_call_needs_sql_execution()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name,
                connection => connection.AllowSqlExecution = false);
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/RefreshOrders/routine/script",
            new { arguments = new Dictionary<string, object?>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
