using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gridlet.AspNetCore.Contracts;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class ForeignKeyDisplayEndpointTests
{
    private const string BaseUrl =
        "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Orders/foreign-key-displays/FK_Orders_Pizzas";

    [Fact]
    public async Task Display_is_opt_in_and_roundtrips_through_structure()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var before = await client.GetFromJsonAsync<JsonElement>(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Orders/structure");
        Assert.Empty(before.GetProperty("foreignKeyDisplays").EnumerateArray());
        Assert.Equal("primaryKey", before.GetProperty("rowIdentity").GetProperty("kind").GetString());

        var save = await client.PostAsJsonAsync(BaseUrl, new { labelColumn = "Name" });
        save.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Orders/structure");
        var display = Assert.Single(after.GetProperty("foreignKeyDisplays").EnumerateArray().ToArray());
        Assert.Equal("FK_Orders_Pizzas", display.GetProperty("foreignKeyName").GetString());
        Assert.Equal("Name", display.GetProperty("labelColumn").GetString());
        Assert.True(display.GetProperty("isValid").GetBoolean());

        var deleted = await client.DeleteAsync(BaseUrl);
        deleted.EnsureSuccessStatusCode();
        after = await client.GetFromJsonAsync<JsonElement>(
            "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo/Orders/structure");
        Assert.Empty(after.GetProperty("foreignKeyDisplays").EnumerateArray());
    }

    [Fact]
    public async Task Lookup_returns_only_requested_keys_and_ranked_search_matches()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        (await client.PostAsJsonAsync(BaseUrl, new { labelColumn = "Name" })).EnsureSuccessStatusCode();

        var resolved = await (await client.PostAsJsonAsync(BaseUrl + "/lookup",
            new { keys = new object[] { 1, 4, 99 } }))
            .Content.ReadFromJsonAsync<ForeignKeyLookupResponse>();
        Assert.Equal(2, resolved!.Items.Count);
        Assert.Contains(resolved.Items, item => item.Key?.ToString() == "1" && item.Label?.ToString() == "Margherita");
        Assert.Contains(resolved.Items, item => item.Key?.ToString() == "4" && item.Label is null);

        var searched = await (await client.PostAsJsonAsync(BaseUrl + "/lookup", new { search = "pe" }))
            .Content.ReadFromJsonAsync<ForeignKeyLookupResponse>();
        var match = Assert.Single(searched!.Items);
        Assert.Equal("Pepperoni", match.Label?.ToString());

        var browsed = await (await client.PostAsJsonAsync(BaseUrl + "/lookup", new { }))
            .Content.ReadFromJsonAsync<ForeignKeyLookupResponse>();
        Assert.Equal(50, browsed!.Items.Count);
    }

    [Fact]
    public async Task Configuration_rejects_unknown_columns_and_lookup_requires_opt_in()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var invalid = await client.PostAsJsonAsync(BaseUrl, new { labelColumn = "DoesNotExist" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var lookup = await client.PostAsJsonAsync(BaseUrl + "/lookup", new { search = "pizza" });
        Assert.Equal(HttpStatusCode.BadRequest, lookup.StatusCode);
    }
}
