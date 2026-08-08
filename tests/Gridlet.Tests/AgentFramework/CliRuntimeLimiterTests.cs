using Gridlet.AgentFramework;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class CliRuntimeLimiterTests
{
    [Fact]
    public async Task Blocks_additional_runtimes_until_an_existing_lease_is_released()
    {
        var limiter = new CliRuntimeLimiter(maximumRuntimes: 1);
        using var first = await limiter.AcquireAsync(CancellationToken.None);

        var secondTask = limiter.AcquireAsync(CancellationToken.None).AsTask();
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(secondTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Lease_release_is_idempotent()
    {
        var limiter = new CliRuntimeLimiter(maximumRuntimes: 1);
        var first = await limiter.AcquireAsync(CancellationToken.None);

        first.Dispose();
        first.Dispose();

        using var second = await limiter.AcquireAsync(CancellationToken.None);
    }
}
