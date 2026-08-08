namespace Gridlet.AgentFramework;

/// <summary>Caps all live subscription-backed CLI runtimes, retained or turn-scoped.</summary>
internal sealed class CliRuntimeLimiter(int maximumRuntimes)
{
    private readonly SemaphoreSlim slots = new(maximumRuntimes, maximumRuntimes);

    public async ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken);
        return new Lease(slots);
    }

    internal sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}

