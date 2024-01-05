using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Enums;
using SportsRankingService.Services.World;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public class WorldRankServiceFactory(Func<IEnumerable<WorldRankService>> factory) : IWorldRankServiceFactory
    {
        private readonly Func<IEnumerable<WorldRankService>> _factory = factory;

        public WorldRankService Create(WorldSports sport)
        {
            var set = _factory();
            string sportName = sport.ToString();
            WorldRankService service = set.First(x => x.GetType().Name.Contains(sportName));

            return service;
        }
    }
}
