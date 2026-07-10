using System.Net;
using System.Net.Http.Json;
using Gridlet.Models;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class SavedQueryEndpointTests
{
    [Fact]
    public async Task Saved_queries_roundtrip_through_the_store()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var saved = await (await client.PostAsJsonAsync("/gridlet/api/queries",
            new { name = "Top customers", connectionName = "Main", database = "FakeDb", sql = "SELECT 1" }))
            .Content.ReadFromJsonAsync<SavedQuery>();
        Assert.NotNull(saved);

        var list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Single(list!);
        Assert.Equal("Top customers", list![0].Name);

        // Overwrite by id keeps a single entry.
        await client.PostAsJsonAsync("/gridlet/api/queries",
            new { id = saved!.Id, name = "Top customers", connectionName = "Main", sql = "SELECT 2" });
        list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Single(list!);
        Assert.Equal("SELECT 2", list![0].Sql);

        var delete = await client.DeleteAsync($"/gridlet/api/queries/{saved.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        list = await client.GetFromJsonAsync<List<SavedQuery>>("/gridlet/api/queries");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Saving_requires_name_and_sql()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/gridlet/api/queries",
            new { name = "", connectionName = "Main", sql = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
