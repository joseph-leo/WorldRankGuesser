using BenchmarkDotNet.Attributes;
using SportsRankingService.Utilities;
using System.Globalization;

namespace WebScrapingBenchmarks.Benchmarks
{
    public class CountryUtilBenchmark
    {
        [Benchmark]
        public List<RegionInfo> GetCountries() => CountryUtil.GetCountries();

        [Benchmark]
        public string IOCToISO3() => "GER".IOCToISO3();
    }
}
