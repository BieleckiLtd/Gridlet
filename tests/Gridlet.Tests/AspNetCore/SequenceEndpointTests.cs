using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Models;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class SequenceEndpointTests
{
    private const string Base = "/gridlet/api/connections/Main/databases/FakeDb";

    [Fact]
    public async Task Reads_creates_and_restarts_sequences_through_the_provider_boundary()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var sequence = await client.GetStringAsync(
            Base + "/objects/dbo/OrderNumbers/sequence");
        Assert.Contains("\"type\":\"Sequence\"", sequence);
        Assert.Contains("\"currentValue\":\"1020\"", sequence);
        Assert.Contains("Order number generator", sequence);

        var create = await client.PostAsJsonAsync(Base + "/sequences",
            new SequenceDesign("dbo", "InvoiceNumbers", StartValue: "10"));
        var restart = await client.PostAsJsonAsync(
            Base + "/objects/dbo/OrderNumbers/sequence/restart", new { value = "2000" });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restart.StatusCode);
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        Assert.Contains("create sequence dbo.InvoiceNumbers", fake.Calls);
        Assert.Contains("restart sequence dbo.OrderNumbers 2000", fake.Calls);
    }
}
