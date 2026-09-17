using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Persistence;

namespace SportsRankingService.Services;

public sealed class RankingUpdater(
    IRankingSourceRunner runner,
    IOptionsMonitor<RankingSourcesOptions> sources,
    IServiceScopeFactory scopeFactory,
    ILogger<RankingUpdater> logger) : IRankingUpdater
{
    public async Task<UpdateSummary> UpdateAllAsync(CancellationToken cancellationToken)
    {
        List<RankingItem> items = sources.CurrentValue.Rankings.Where(i => i.Enabled).ToList();

        if (items.Count == 0)
        {
            logger.LogWarning("No enabled ranking items configured; check serviceconfig.json");
            return new UpdateSummary(0, 0, 0, 0);
        }

        SaveOutcome?[] outcomes = await Task.WhenAll(items.Select(item => UpdateItemAsync(item, cancellationToken)));

        UpdateSummary summary = new(
            Feeds: items.Count,
            Inserted: outcomes.Count(o => o == SaveOutcome.Inserted),
            Unchanged: outcomes.Count(o => o == SaveOutcome.Unchanged),
            Failed: outcomes.Count(o => o is null));

        logger.LogInformation("{Feeds} feeds: {Inserted} new releases, {Unchanged} unchanged, {Failed} failed",
            summary.Feeds, summary.Inserted, summary.Unchanged, summary.Failed);

        return summary;
    }

    /// <returns>The save outcome, or null when the feed produced nothing or threw.</returns>
    private async Task<SaveOutcome?> UpdateItemAsync(RankingItem item, CancellationToken cancellationToken)
    {
        string name = RankingSourceRunner.Describe(item);

        try
        {
            RankingSnapshot? snapshot = await runner.RunAsync(item, cancellationToken);

            if (snapshot is null || snapshot.Entries.Count == 0)
            {
                logger.LogWarning("No rows for {Item}; nothing saved", name);
                return null;
            }

            // The updater is transient but the DbContext is scoped, so each feed gets its own scope.
            using IServiceScope scope = scopeFactory.CreateScope();
            IRankingRepository repository = scope.ServiceProvider.GetRequiredService<IRankingRepository>();
            SaveOutcome outcome = await repository.SaveAsync(snapshot, cancellationToken);

            logger.LogInformation("{Item}: {Outcome} ({Count} rows, ranking date {Date})", name, outcome, snapshot.Entries.Count, snapshot.RankingDate);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feed {Item} failed; other feeds are unaffected", name);
            return null;
        }
    }
}
