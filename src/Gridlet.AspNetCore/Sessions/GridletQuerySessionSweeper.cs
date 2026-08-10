using Gridlet.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gridlet.AspNetCore.Sessions;

/// <summary>
/// Closes pinned query sessions that have gone idle. Without this, a browser tab that was closed
/// without ending its transaction would leave one open on the server, holding locks until the host
/// process restarts.
/// </summary>
internal sealed class GridletQuerySessionSweeper(
    GridletQuerySessionManager sessions,
    ILogger<GridletQuerySessionSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await sessions.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A session that refuses to close must not stop the sweeper from trying again.
                logger.LogWarning(ex, "Gridlet could not close idle query sessions.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
