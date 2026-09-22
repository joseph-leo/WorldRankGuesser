using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

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
    /// <summary>Loads before the host finishes starting, so a started host either has rankings or has logged why not.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(rankingsOptions.Value.RefreshMinutes), time);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IRankingsReader>().ReadAsync(ct);
            var snapshot = RankingsSnapshotBuilder.Build(rows, gameOptions.Value, scoringOptions.Value.Cap, catalog, time.GetUtcNow());

            store.Set(snapshot);
            logger.LogInformation(
                "Rankings loaded: {Rows} rows, {Countries} drawable countries.", rows.Count, snapshot.DrawableCountries.Count);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Keep serving the previous snapshot; /readyz reports not-ready only if there has never been one.
            logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
        }
    }
}
