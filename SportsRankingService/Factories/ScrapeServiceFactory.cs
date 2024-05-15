using Microsoft.Extensions.DependencyInjection;
using SportsRankingService.Enums;
using SportsRankingService.Services;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public class ScrapeServiceFactory(Func<IEnumerable<IScrapeService>> factory) : IScrapeServiceFactory
    {
        private readonly Func<IEnumerable<IScrapeService>> _factory = factory;

        public IScrapeService Create(RankingType rankingType)
        {
            var set = _factory();
            string rankingName = rankingType.ToString();
            IScrapeService service = set.First(x => x.GetType().Name.Contains(rankingName));

            return service;
        }
    }
}
