using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

/// <summary>The HTTP surface of pinned query sessions.</summary>
public sealed class SessionEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb";

    [Fact]
    public async Task A_session_runs_queries_and_reports_its_transaction_state()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<Gridlet.Abstractions.IGridletProvider>();

        var opened = await ReadAsync(await client.PostAsync($"{Base}/sessions", null));
        var id = opened.GetProperty("id").GetString()!;
        Assert.False(opened.GetProperty("transaction").GetProperty("isOpen").GetBoolean());

        var begun = await ReadAsync(await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/transaction", new { command = "begin" }));
        Assert.True(begun.GetProperty("transaction").GetProperty("isOpen").GetBoolean());
        Assert.Equal(1, begun.GetProperty("transaction").GetProperty("depth").GetInt32());

        var query = await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/query", new { sql = "SELECT 42" });
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Contains("\"type\":\"completed\"", await query.Content.ReadAsStringAsync());

        var committed = await ReadAsync(await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/transaction", new { command = "commit" }));
        Assert.False(committed.GetProperty("transaction").GetProperty("isOpen").GetBoolean());

        var closed = await client.DeleteAsync($"/gridlet/api/sessions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.Equal(
            ["session.open Main/FakeDb", "session.begin", "session.query SELECT 42", "session.commit"],
            fake.Calls.Where(call => call.StartsWith("session.", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task A_closed_session_stops_answering()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var opened = await ReadAsync(await client.PostAsync($"{Base}/sessions", null));
        var id = opened.GetProperty("id").GetString()!;
        await client.DeleteAsync($"/gridlet/api/sessions/{id}");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/gridlet/api/sessions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/gridlet/api/sessions/{id}")).StatusCode);
        var query = await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/query", new { sql = "SELECT 42" });
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
        Assert.Contains("no longer open", await query.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A session outlives the configuration it was opened against. If the connection is removed
    /// while it is open, the next statement has to come back as a clean 404 before the NDJSON
    /// stream starts, not as an unhandled failure.
    /// </summary>
    [Fact]
    public async Task A_session_whose_connection_has_gone_answers_404()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var opened = await ReadAsync(await client.PostAsync($"{Base}/sessions", null));
        var id = opened.GetProperty("id").GetString()!;

        var options = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<GridletOptions>>();
        options.CurrentValue.Connections.Clear();

        var query = await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/query", new { sql = "SELECT 42" });

        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
        Assert.Contains("Main", await query.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_session_id_is_not_confirmed()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.GetAsync("/gridlet/api/sessions/deadbeef");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_transaction_command_has_to_be_one_of_the_three()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var opened = await ReadAsync(await client.PostAsync($"{Base}/sessions", null));
        var id = opened.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync(
            $"/gridlet/api/sessions/{id}/transaction", new { command = "drop database" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sessions_are_refused_where_sql_execution_is_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name,
                connection => connection.AllowSqlExecution = false);
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsync($"{Base}/sessions", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_session_limit_is_enforced()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name);
            options.Security.AllowAnonymous = true;
            options.Limits.MaxQuerySessions = 1;
        });
        await using var _ = app;

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Base}/sessions", null)).StatusCode);
        var second = await client.PostAsync($"{Base}/sessions", null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("Close one", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Open_sessions_are_listed()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var opened = await ReadAsync(await client.PostAsync($"{Base}/sessions", null));

        var listed = await ReadAsync(await client.GetAsync("/gridlet/api/sessions"));

        Assert.Equal(opened.GetProperty("id").GetString(), listed[0].GetProperty("id").GetString());
        Assert.Equal("Main", listed[0].GetProperty("connectionName").GetString());
        Assert.Equal("FakeDb", listed[0].GetProperty("database").GetString());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
