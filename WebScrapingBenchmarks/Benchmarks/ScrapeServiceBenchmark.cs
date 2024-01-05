using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Services.World;
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
        private WorldRankService _rugbyService;
        public ScrapeServiceBenchmark() 
        {
            var logger = new LoggerFactory().CreateLogger<BaseballService>();
            _rugbyService = new BaseballService(logger);
        }

        [Benchmark]
        public async Task GetSportRanksAsyncBenchmark()
        {
            await _rugbyService.GetSportRanksAsync();
        }
    }
}
