using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// Loads the rankings snapshot in the background: at once, then on a short backoff until the first load succeeds
/// (a cold start usually meets a database that is still resuming), then every Rankings:RefreshMinutes. A failed
/// refresh keeps the previous snapshot. Host startup never waits for a load, so a startup probe cannot trip on it;
/// /readyz reports not-ready until the first snapshot exists.
/// </summary>
public sealed class RankingsRefreshService(
    IServiceScopeFactory scopes,
    IRankingsStore store,
    IOptions<GameOptions> gameOptions,
    IOptions<ScoringOptions> scoringOptions,
    IOptions<RankingsOptions> rankingsOptions,
    CountryCatalog catalog,
    TimeProvider time,
    ILogger<RankingsRefreshService> logger) : BackgroundService
{
    /// <summary>Waits between attempts while there is no snapshot yet; the last one repeats.</summary>
    private static readonly TimeSpan[] StartupBackoff =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; !await RefreshAsync(stoppingToken); attempt++)
        {
            await Task.Delay(StartupBackoff[Math.Min(attempt, StartupBackoff.Length - 1)], time, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    /// <summary>True when a snapshot was stored.</summary>
    private async Task<bool> RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IRankingsReader>().ReadAsync(ct);
            var snapshot = RankingsSnapshotBuilder.Build(rows, gameOptions.Value, scoringOptions.Value.Cap, catalog, time.GetUtcNow());

            // A view the scraper has not filled yet cannot draw a board (BoardGenerator needs one country per category);
            // treating it as loaded would make /readyz say ready and every game start fail.
            if (snapshot.DrawableCountries.Count < snapshot.Categories.Count)
            {
                throw new InvalidOperationException(
                    $"The rankings view yields {snapshot.DrawableCountries.Count} drawable countries; a board needs {snapshot.Categories.Count}.");
            }

            store.Set(snapshot);
            logger.LogInformation(
                "Rankings loaded: {Rows} rows, {Countries} drawable countries.", rows.Count, snapshot.DrawableCountries.Count);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A cold start meeting a resuming database is expected, so those attempts log a warning without
            // claiming a previous snapshot; a failure after a snapshot exists is not expected and stays an error.
            if (store.Current is null)
            {
                logger.LogWarning(error, "Loading the rankings failed; no snapshot yet, trying again.");
            }
            else
            {
                logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
            }

            return false;
        }
    }
}
