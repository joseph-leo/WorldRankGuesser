using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.RankingsDb;

namespace SportsRankingService.Services
{
    public sealed class RankingUpdater(
        RankingSourceRunner runner,
        IOptionsMonitor<RankingSourcesOptions> sources,
        IServiceScopeFactory scopeFactory,
        ILogger<RankingUpdater> logger) : IRankingUpdater
    {
        public async Task UpdateAllAsync(CancellationToken cancellationToken)
        {
            List<RankingItem> items = sources.CurrentValue.Rankings.Where(i => i.Enabled).ToList();

            if (items.Count == 0)
            {
                logger.LogWarning("No enabled ranking items configured; check serviceconfig.json");
                return;
            }

            int[] inserted = await Task.WhenAll(items.Select(item => UpdateItemAsync(item, cancellationToken)));

            logger.LogInformation("{Rows} rows inserted across {Feeds} feeds ({Failed} yielded nothing)",
                inserted.Sum(), items.Count, inserted.Count(n => n == 0));
        }

        private async Task<int> UpdateItemAsync(RankingItem item, CancellationToken cancellationToken)
        {
            string name = RankingSourceRunner.Describe(item);

            try
            {
                IReadOnlyList<SportsRanking> rows = await runner.RunAsync(item, cancellationToken);

                if (rows.Count == 0)
                {
                    logger.LogWarning("No rows for {Item}; nothing inserted", name);
                    return 0;
                }

                // The updater is transient but the DbContext is scoped, so each feed gets its own scope.
                using IServiceScope scope = scopeFactory.CreateScope();
                WorldRankGuesserContext db = scope.ServiceProvider.GetRequiredService<WorldRankGuesserContext>();
                await db.SportsRankings.AddRangeAsync(rows, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                logger.LogInformation("{Count} rows inserted for {Item}", rows.Count, name);
                return rows.Count;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Feed {Item} failed; other feeds are unaffected", name);
                return 0;
            }
        }
    }
}
