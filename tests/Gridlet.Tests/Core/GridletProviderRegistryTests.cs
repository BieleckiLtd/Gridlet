using Gridlet.Tests.AspNetCore.Fakes;
using Xunit;

namespace Gridlet.Tests.Core;

public class GridletProviderRegistryTests
{
    [Fact]
    public void Resolves_providers_case_insensitively()
    {
        var registry = new GridletProviderRegistry([new FakeGridletProvider()]);

        Assert.Same(registry.Get("fake"), registry.Get("FAKE"));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Unknown_provider_throws()
    {
        var registry = new GridletProviderRegistry([]);

        var ex = Assert.Throws<GridletUnknownProviderException>(() => registry.Get("Postgres"));
        Assert.Equal("Postgres", ex.ProviderName);
    }
}
