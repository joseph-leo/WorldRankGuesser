using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SportsRankingService.Enums;
using SportsRankingService.Factories;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.RankingsDb;
using SportsRankingService.Repository;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Services
{
    public class RankingUpdater(ILogger<RankingUpdater> logger, IScrapeServiceFactory scrapeServiceFactory, IServiceScopeFactory serviceScopeFactory) : IRankingUpdater
    {
        private readonly ILogger<RankingUpdater> _logger = logger;
        private readonly IScrapeServiceFactory _scrapeServiceFactory = scrapeServiceFactory;
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
        private int count = 0;

        public async Task UpdateRankingsAsync(RankingType rankingType, CancellationToken stoppingToken)
        {
            switch (rankingType)
            {
                case RankingType.World:
                    await UpdateAllWorldRanks(stoppingToken);
                    break;
            }
            _logger.LogInformation("{Count} rows inserted to {Table} since startup", count, nameof(SportsRanking));
        }

        public async Task UpdateWorldRankAsync(WorldSports sport, CancellationToken stoppingToken)
        {
            try
            {
                IScrapeService service = _scrapeServiceFactory.Create(RankingType.World);
                List<IRanking> rankings = (await service.GetSportRanksAsync(sport)).ToList();

                if (rankings.Count == 0)
                {
                    _logger.LogWarning("No rankings parsed for {Sport}; nothing inserted", sport);
                    return;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorldRankGuesserContext>();
                await dbContext.AddRangeAsync(rankings, stoppingToken);
                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("{Count} {Sport} rows inserted to {Table}", rankings.Count, sport, nameof(SportsRanking));
                Interlocked.Add(ref count, rankings.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Updating {Sport} failed; other sports are unaffected", sport);
            }
        }

        private async Task UpdateAllWorldRanks(CancellationToken stoppingToken)
        {
            List<Task> tasks = [];
            foreach (WorldSports sport in Enum.GetValues<WorldSports>())
            {
                tasks.Add(UpdateWorldRankAsync(sport, stoppingToken));
            }

            await Task.WhenAll(tasks);
        }
    }
}
