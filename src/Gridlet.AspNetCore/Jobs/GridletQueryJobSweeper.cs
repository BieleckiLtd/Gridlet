using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gridlet.AspNetCore;

internal sealed class GridletQueryJobSweeper(
    GridletQueryJobManager jobs,
    ILogger<GridletQueryJobSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                jobs.Sweep();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background query-job cleanup stopped unexpectedly.");
        }
    }
}
