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

        private readonly string menRankDateURL = "https://www.fifa.com/fifa-world-ranking/men";
        private readonly string womenRankDateURL = "https://www.fifa.com/fifa-world-ranking/women";

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

        public virtual async Task<string> CallUrlAsync(string url)
        {
            _logger.LogInformation("Fetching. {Time}", Environment.CurrentManagedThreadId);

            HttpClient httpClient = new();

            using var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            return null;
        }

        private async Task<IEnumerable<IRanking>> FetchAndParseAsync(RankingItem rankingItem, IParser parser)
        {
            string url = rankingItem.Url;

            if (rankingItem.Sport == "Soccer/Football")
            {
                string id = await GetLatestId(rankingItem.Gender);

                url = string.Format(url, id);
            }

            string response = await CallUrlAsync(url);
            _logger.LogInformation("Fetched. {ClassName} {Time}", rankingItem.Sport + " " + rankingItem.Event, Environment.CurrentManagedThreadId);

            IEnumerable<IRanking> rankings = parser.ParseResponse(response, rankingItem);
            _logger.LogInformation("Parsed. {ClassName} {Time}", rankingItem.Sport + " " + rankingItem.Event, Environment.CurrentManagedThreadId);

            IEnumerable<IRanking> bad = rankings.Where(x => x.ISO3.Length > 3);

            if (bad.Any())
            {

            }

            return rankings;
        }

        private List<RankingItem> GetRankingItems(WorldSports sport)
        {
            List<RankingItem> rankingItems = _config.GetRankingSection(sport.ToString());

            return rankingItems;
        }

        private async Task<List<SoccerRankDate>> GetRankDatesAndIds(string url)
        {
            string response = await CallUrlAsync(url);
            HtmlDocument html = new();
            html.LoadHtml(response);

            string scriptContent = html.DocumentNode.SelectSingleNode("//script[contains(., \"dates\")]/text()").InnerText;

            JObject scriptJson = JObject.Parse(scriptContent);
            JToken props = scriptJson["props"].NotNullOrEmpty();
            JToken pageProps = props["pageProps"].NotNullOrEmpty();
            JToken pageData = pageProps["pageData"].NotNullOrEmpty();
            JToken ranking = pageData["ranking"].NotNullOrEmpty();
            JToken dates = ranking["dates"].NotNullOrEmpty();

            List<SoccerRankDate> rankDate = JsonSerializer.Deserialize<List<SoccerRankDate>>(dates.ToString()).NotNullOrEmpty();

            return rankDate;
        }

        private async Task<string> GetLatestId(string gender)
        {
            string url = gender == "Men" ? menRankDateURL : womenRankDateURL;
            List<SoccerRankDate> soccerRankDates = await GetRankDatesAndIds(url);
            string id = soccerRankDates.First().id;

            return id;
        }
    }
}
