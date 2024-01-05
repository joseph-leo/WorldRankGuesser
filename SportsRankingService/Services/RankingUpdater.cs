using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Enums;
using SportsRankingService.Factories;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.RankingsDb;
using SportsRankingService.Repository;
using SportsRankingService.Services.World;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Services
{
    public class RankingUpdater(IWorldRankServiceFactory worldRankFactory, IWorldRankRepository repository) : IRankingUpdater
    {
        private readonly IWorldRankServiceFactory _worldRankFactory = worldRankFactory;
        private readonly IWorldRankRepository _repository = repository;

        public async Task UpdateRankingsAsync(RankingType rankingType)
        {
            switch (rankingType)
            {
                case RankingType.World:
                    await UpdateAllWorldRanks();
                    break;
            }
        }

        public async Task UpdateWorldRankAsync(WorldSports sport)
        {
            WorldRankService service = _worldRankFactory.Create(sport);
            IEnumerable<IRanking> rankings = await service.GetSportRanksAsync();
            await _repository.AddRangeAsync(rankings);
        }

        private async Task UpdateAllWorldRanks()
        {
            List<Task> tasks = [];
            foreach (WorldSports sport in Enum.GetValues(typeof(WorldSports)))
            {
                tasks.Add(UpdateWorldRankAsync(sport));
            }

            await Task.WhenAll(tasks);
        }
    }
}
