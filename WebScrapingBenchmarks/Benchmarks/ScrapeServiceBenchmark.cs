using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Services;

namespace SportsRankingService.Benchmark
{
    /// <summary>Fetches and parses every enabled feed in serviceconfig.json through the real pipeline (live URLs, no database).</summary>
    [MemoryDiagnoser]
    public class ScrapeServiceBenchmark
    {
        private readonly RankingSourceRunner _runner;
        private readonly List<RankingItem> _items;

        public ScrapeServiceBenchmark()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "serviceconfig.json"), optional: false)
                .Build();

            ServiceCollection services = new();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            services.Configure<RankingSourcesOptions>(configuration);
            services.AddRankingPipeline();

            ServiceProvider provider = services.BuildServiceProvider();
            _runner = provider.GetRequiredService<RankingSourceRunner>();
            _items = provider.GetRequiredService<IOptions<RankingSourcesOptions>>().Value.Rankings.Where(i => i.Enabled).ToList();
        }

        [Benchmark]
        public async Task<int> FetchAndParseAllFeeds()
        {
            int rows = 0;
            foreach (RankingItem item in _items)
            {
                rows += (await _runner.RunAsync(item, CancellationToken.None))?.Entries.Count ?? 0;
            }

            return rows;
        }
    }
}
