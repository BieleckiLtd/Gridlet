using System.Net;
using System.Net.Http.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class QueryPlanEndpointTests
{
    private const string Plan = "/gridlet/api/connections/Main/databases/FakeDb/query/plan";

    [Fact]
    public async Task An_estimated_plan_comes_back_as_a_tree()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<Gridlet.Abstractions.IGridletProvider>();

        var response = await client.PostAsJsonAsync(Plan, new { sql = "SELECT 1" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"mode\":\"estimated\"", body);
        Assert.Contains("\"operation\":\"Clustered Index Scan\"", body);
        Assert.Contains("\"warnings\":[\"Missing index on Customers (Name)\"]", body);
        Assert.Contains("plan.estimated SELECT 1", fake.Calls);
    }

    [Fact]
    public async Task An_actual_plan_carries_runtime_rows_and_statistics_messages()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Plan, new { sql = "SELECT 1", mode = "actual" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"mode\":\"actual\"", body);
        Assert.Contains("\"actualRows\":118", body);
        Assert.Contains("logical reads 3", body);
    }

    [Fact]
    public async Task An_unknown_mode_is_rejected()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Plan, new { sql = "SELECT 1", mode = "guess" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_failing_statement_reports_the_engines_message()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Plan, new { sql = "boom" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kaboom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Plans_need_sql_execution()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name,
                connection => connection.AllowSqlExecution = false);
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync(Plan, new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
