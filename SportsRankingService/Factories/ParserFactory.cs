using SportsRankingService.Enums;
using SportsRankingService.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public class ParserFactory(Func<IEnumerable<IParser>> factory) : IParserFactory
    {
        private readonly Func<IEnumerable<IParser>> _factory = factory;

        public IParser Create(WorldSports sport)
        {
            var parsers = _factory();

            string sportName = sport.ToString();
            IParser service = parsers.First(x => x.GetType().Name.Contains(sportName));

            return service;
        }
    }
}
