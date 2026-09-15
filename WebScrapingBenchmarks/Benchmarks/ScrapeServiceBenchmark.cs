using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using SportsRankingService.Factories;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Benchmark
{
    [MemoryDiagnoser]
    public class ScrapeServiceBenchmark
    {
        private IScrapeServiceFactory _scrapeServiceFactory;
        public ScrapeServiceBenchmark() 
        {
            var logger = new LoggerFactory().CreateLogger<WorldRankService>();
            var rugbyLogger = new LoggerFactory().CreateLogger<RugbyParser>();
            var cricketLogger = new LoggerFactory().CreateLogger<CricketParser>();
            var tennisLogger = new LoggerFactory().CreateLogger<TennisParser>();
            var soccerLogger = new LoggerFactory().CreateLogger<SoccerParser>();

            IEnumerable<IParser> parsers = new List<IParser>()
            {
                new RugbyParser(rugbyLogger),
                new CricketParser(cricketLogger),
                new TennisParser(tennisLogger),
                new SoccerParser(soccerLogger)
            };

            IParserFactory parserFactory = new ParserFactory(() => parsers);

            IEnumerable<IScrapeService> scrapeServices =
            [
                new WorldRankService(logger, parserFactory)
            ];
            _scrapeServiceFactory = new ScrapeServiceFactory(() => scrapeServices);
        }

        [Benchmark]
        public async Task GetSportRanksAsyncBenchmark()
        {
            IScrapeService rankService = _scrapeServiceFactory.Create(Enums.RankingType.World);
            await rankService.GetSportRanksAsync(Enums.WorldSports.Soccer);
        }
    }
}
