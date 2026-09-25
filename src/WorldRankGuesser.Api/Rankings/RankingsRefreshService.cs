using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// Loads the rankings snapshot in the background: at once, then on a short backoff until the first load succeeds
/// (a cold start usually meets a database that is still resuming), then every Rankings:RefreshMinutes. Host startup
/// never waits for a load, so a startup probe cannot trip on it; /readyz reports not-ready until the first snapshot
/// exists. The load itself is <see cref="RankingsRefresher"/>, shared with the refresh endpoint.
/// </summary>
public sealed class RankingsRefreshService(
    RankingsRefresher refresher,
    IOptions<RankingsOptions> rankingsOptions,
    TimeProvider time) : BackgroundService
{
    /// <summary>Waits between attempts while there is no snapshot yet; the last one repeats.</summary>
    private static readonly TimeSpan[] StartupBackoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; !(await refresher.RefreshAsync(stoppingToken)).Loaded; attempt++)
        {
            await Task.Delay(StartupBackoff[Math.Min(attempt, StartupBackoff.Length - 1)], time, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await refresher.RefreshAsync(stoppingToken);
        }
    }
}
