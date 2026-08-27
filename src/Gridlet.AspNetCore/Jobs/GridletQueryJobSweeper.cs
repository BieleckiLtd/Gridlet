using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gridlet.AspNetCore;

internal sealed class GridletQueryJobSweeper(
    GridletQueryJobManager jobs,
    ILogger<GridletQueryJobSweeper> logger,
    TimeSpan? sweepInterval = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(sweepInterval ?? TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    jobs.Sweep();
                }
                catch (Exception ex)
                {
                    // A transient options reload or cleanup failure must not permanently disable
                    // retention enforcement. The next tick retries against current state.
                    logger.LogError(ex, "Background query-job cleanup failed; it will be retried.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
