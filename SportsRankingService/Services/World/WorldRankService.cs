using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Factories;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace SportsRankingService.Services.World
{
    public abstract class WorldRankService : IScrapeService<WorldRankInfo>
    {
        protected Dictionary<string, Dictionary<string, Dictionary<string, string>>> Urls { get; set; }

        protected readonly ILogger<WorldRankService> _logger;
        private readonly ConfigHelper _config;

        private readonly string menRankDateURL = "https://www.fifa.com/fifa-world-ranking/men";
        private readonly string womenRankDateURL = "https://www.fifa.com/fifa-world-ranking/women";

        public WorldRankService(ILogger<WorldRankService> logger)
        {
            _logger = logger;
            _config = new("serviceconfig");
            Urls = MapUrls();
        }

        public async Task<IEnumerable<IRanking>> GetSportRanksAsync()
        {
            List<Task<IEnumerable<IRanking>>> tasks = [];

            foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> sport in Urls)
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> _event in sport.Value)
                {
                    string? eventName = _event.Key == "N/A" ? null : _event.Key;

                    foreach (KeyValuePair<string, string> gender in _event.Value)
                    {
                        string url = gender.Value;
                        IRanking rankingPrototype = RankingPrototypeFactory.CreateWorldRanking(gender.Key, _event.Key, sport.Key);
                        tasks.Add(FetchAndParseAsync(url, rankingPrototype));
                    }
                }
            }

            IEnumerable<IRanking>[] result = await Task.WhenAll(tasks);

            IEnumerable<IRanking> allRanks = result.SelectMany(x => x);

            return allRanks;
        }

        public abstract IEnumerable<IRanking> ParseResponse(string response, IRanking prototype);

        public virtual async Task<string> CallUrlAsync(string url)
        {
            _logger.LogInformation("Fetching. {Time}", Environment.CurrentManagedThreadId);

            HttpClient? httpClient = new();

            using var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            return null;
        }

        private async Task<IEnumerable<IRanking>> FetchAndParseAsync(string url, IRanking prototype)
        {
            if (prototype.Sport == "Soccer/Football")
            {
                string id = await GetLatestId(prototype.Gender);
                url = string.Format(url, id);
            }

            string response = await CallUrlAsync(url);
            _logger.LogInformation("Fetched. {ClassName} {Time}", prototype.Sport + " " + prototype.Sport, Environment.CurrentManagedThreadId);

            IEnumerable<IRanking> rankings = ParseResponse(response, prototype);
            _logger.LogInformation("Parsed. {ClassName} {Time}", prototype.Sport + " " + prototype.Sport, Environment.CurrentManagedThreadId);

            return rankings;
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> MapUrls()
        {
            var urlsDict = _config.Urls;
            var sportsDict = urlsDict[GetType().Name];

            return sportsDict;
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
