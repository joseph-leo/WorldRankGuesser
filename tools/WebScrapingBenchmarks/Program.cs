using BenchmarkDotNet.Running;
using SportsRankingService.Benchmark;
using SportsRankingService.Services;
using WebScrapingBenchmarks.Benchmarks;

var summary = BenchmarkRunner.Run<ScrapeServiceBenchmark>();