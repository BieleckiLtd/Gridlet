using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class RenameAndTruncateEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb/objects/dbo";

    [Fact]
    public async Task Objects_and_indexes_are_renamed_through_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var table = await client.PostAsJsonAsync(
            $"{Base}/Customers/rename?type=Table", new { newName = "Clients" });
        var view = await client.PostAsJsonAsync(
            $"{Base}/vw_Orders/rename?type=View", new { newName = "vw_Sales" });
        var index = await client.PostAsJsonAsync(
            $"{Base}/Customers/indexes/IX_Customers_Name/rename", new { newName = "IX_Clients_Name" });

        // Renames answer like the rest of the DDL routes.
        Assert.Equal(HttpStatusCode.OK, table.StatusCode);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("renameObject Table dbo.Customers -> Clients", fake.Calls);
        Assert.Contains("renameObject View dbo.vw_Orders -> vw_Sales", fake.Calls);
        Assert.Contains("renameIndex dbo.Customers.IX_Customers_Name -> IX_Clients_Name", fake.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_rename_needs_a_name(string newName)
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/Customers/rename", new { newName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_needs_ddl_permission()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name,
                connection => connection.AllowDdl = false);
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsJsonAsync($"{Base}/Customers/rename", new { newName = "Clients" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Emptying_a_table_reaches_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.PostAsync($"{Base}/Customers/truncate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("truncate dbo.Customers", fake.Calls);
    }

    /// <summary>
    /// Emptying a table destroys data but changes no schema, so it follows the write permission
    /// rather than the DDL one.
    /// </summary>
    [Fact]
    public async Task Emptying_a_table_needs_write_permission_not_ddl()
    {
        var (app, client) = await GridletTestHost.StartAsync(options =>
        {
            options.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, connection =>
            {
                connection.AllowWrites = false;
                connection.AllowDdl = true;
            });
            options.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var response = await client.PostAsync($"{Base}/Customers/truncate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
