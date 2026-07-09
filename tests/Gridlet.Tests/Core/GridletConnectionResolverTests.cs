using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.Core;

public class GridletConnectionResolverTests
{
    private static IGridletConnectionResolver CreateResolver()
    {
        var services = new ServiceCollection();
        services.AddGridletCore(o =>
            o.AddConnection("Main", "Server=fake;", FakeGridletProvider.Name));
        services.AddSingleton<IGridletProvider, FakeGridletProvider>();
        return services.BuildServiceProvider().GetRequiredService<IGridletConnectionResolver>();
    }

    [Fact]
    public void Resolves_connection_with_provider_and_database()
    {
        var resolved = CreateResolver().Resolve("main", "SomeDb");

        Assert.Equal(FakeGridletProvider.Name, resolved.Provider.ProviderName);
        Assert.Equal("Main", resolved.Context.ConnectionName);
        Assert.Equal("SomeDb", resolved.Context.Database);
    }

    [Fact]
    public void Unknown_connection_throws()
    {
        Assert.Throws<GridletUnknownConnectionException>(() => CreateResolver().Resolve("nope"));
    }
}
