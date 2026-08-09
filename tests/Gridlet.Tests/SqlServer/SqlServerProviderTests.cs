using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerProviderTests
{
    [Fact]
    public void Advertises_schema_fidelity_capabilities()
    {
        var capabilities = new SqlServerGridletProvider().Capabilities;

        Assert.True(capabilities.SupportsCheckConstraints);
        Assert.True(capabilities.SupportsUniqueConstraints);
        Assert.True(capabilities.SupportsIndexes);
    }
}
