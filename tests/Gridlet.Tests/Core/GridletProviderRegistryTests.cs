using Gridlet.Tests.AspNetCore.Fakes;
using Xunit;

namespace Gridlet.Tests.Core;

public class GridletProviderRegistryTests
{
    [Fact]
    public void Resolves_providers_by_enum_value()
    {
        var registry = new GridletProviderRegistry([new FakeGridletProvider()]);

        Assert.Same(registry.Get(GridletProviderNames.SqlServer), registry.Get(GridletProviderNames.SqlServer));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Unknown_provider_throws()
    {
        var registry = new GridletProviderRegistry([]);

        var ex = Assert.Throws<GridletUnknownProviderException>(() => registry.Get(GridletProviderNames.Sqlite));
        Assert.Equal(GridletProviderNames.Sqlite, ex.ProviderName);
    }
}
