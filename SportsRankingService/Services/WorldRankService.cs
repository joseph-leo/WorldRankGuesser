using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Enums;
using SportsRankingService.Factories;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace SportsRankingService.Services
{
    public class WorldRankService(ILogger<WorldRankService> logger, IParserFactory parserFactory) : IScrapeService
    {
        private readonly ILogger<WorldRankService> _logger = logger;
        private readonly IParserFactory _parserFactory = parserFactory;
        private readonly ConfigHelper _config = new("serviceconfig");

        // www.fifa.com/fifa-world-ranking/* now redirects here; the page still embeds the ranking dates as page data.
        private const string menRankDateURL = "https://inside.fifa.com/fifa-rankings/world-ranking/men";
        private const string womenRankDateURL = "https://inside.fifa.com/fifa-rankings/world-ranking/women";

        public async Task<IEnumerable<IRanking>> GetSportRanksAsync(WorldSports sport)
        {
            List<Task<IEnumerable<IRanking>>> tasks = [];

            IParser parser = _parserFactory.Create(sport);

            List<RankingItem> rankingItems = GetRankingItems(sport);

            foreach (RankingItem item in rankingItems)
            {
                tasks.Add(FetchAndParseAsync(item, parser));
            }

            IEnumerable<IRanking>[] result = await Task.WhenAll(tasks);

            IEnumerable<IRanking> allRanks = result.SelectMany(x => x);

            return allRanks;
        }

        // Several federation sites (ATP, FIBA, IIHF) return 403 to a request with no browser User-Agent.
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36";

        /// <summary>
        /// Fetches <paramref name="url"/> and returns the body, or null on a non-success status or a transport failure.
        /// Never throws for a network problem so that one bad source cannot fault the whole tick.
        /// </summary>
        public virtual async Task<string?> CallUrlAsync(string url)
        {
            _logger.LogInformation("Fetching {Url}", url);

            try
            {
                using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");

                using var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                _logger.LogWarning("Fetch failed. {Url} returned {StatusCode}", url, (int)response.StatusCode);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Fetch failed. {Url}", url);
                return null;
            }
        }

        private async Task<IEnumerable<IRanking>> FetchAndParseAsync(RankingItem rankingItem, IParser parser)
        {
            try
            {
                string url = rankingItem.Url;

                if (rankingItem.Sport == "Soccer/Football")
                {
                    string id = await GetLatestId(rankingItem.Gender.NotNullOrEmpty());

                    url = string.Format(url, id);
                }

                string? response = await CallUrlAsync(url);

                if (response is null)
                {
                    return [];
                }

                _logger.LogInformation("Fetched {Sport} {Event} {Gender}", rankingItem.Sport, rankingItem.Event, rankingItem.Gender);

                IEnumerable<IRanking> rankings = parser.ParseResponse(response, rankingItem);

                _logger.LogInformation("Parsed {Count} rows for {Sport} {Event} {Gender}", rankings.Count(), rankingItem.Sport, rankingItem.Event, rankingItem.Gender);

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch or parse {Sport} {Event} {Gender}", rankingItem.Sport, rankingItem.Event, rankingItem.Gender);
                return [];
            }
        }

        private List<RankingItem> GetRankingItems(WorldSports sport)
        {
            return GetRankingItems(sport.ToString());
        }

        private List<RankingItem> GetRankingItems(string sport)
        {
            var rankingItems = _config.GetConfigSection<List<RankingItem>>(sport);
            rankingItems ??= [];

            return rankingItems;
        }

        private async Task<string> GetLatestId(string gender)
        {
            string url = gender == "Men" ? menRankDateURL : womenRankDateURL;
            string page = (await CallUrlAsync(url)).NotNullOrEmpty();

            return UrlResolvers.FifaDateIdResolver.ExtractLatestDateId(page);
        }
    }
}
