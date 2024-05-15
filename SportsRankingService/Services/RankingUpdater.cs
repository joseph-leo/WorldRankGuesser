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
            _logger.LogInformation("Finished inserting to {table}", typeof(SportsRanking));

           _logger.LogInformation("{count} total inserted to {table}", count, typeof(SportsRanking));
        }

        public async Task UpdateWorldRankAsync(WorldSports sport, CancellationToken stoppingToken)
        {
            IScrapeService service = _scrapeServiceFactory.Create(RankingType.World);
            IEnumerable<IRanking> rankings = await service.GetSportRanksAsync(sport);



            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<WorldRankGuesserContext>().NotNullOrEmpty();
            await dbContext.AddRangeAsync(rankings, stoppingToken);
            await dbContext.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("{count} inserted to {table}", rankings.Count(), typeof(SportsRanking));
            count += rankings.Count();
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
