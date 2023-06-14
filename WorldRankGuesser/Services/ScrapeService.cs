using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class ScrapeService<TRank, TRankModel> where TRank : Ranking, new()
    {
        protected virtual Dictionary<string, Dictionary<string, string>> Urls { get => MapUrls(); }

        protected string? Sport { get; set; }
        protected string? Gender { get; set; }

        public async Task<TRank> GetLowestRankAsync(string ISO3)
        {
            List<TRank> allCountryRankings = await GetRankingsAsync(ISO3);
            TRank lowestRank = allCountryRankings[0];

            foreach (var rank in allCountryRankings)
            {
                lowestRank = rank.Rank < lowestRank.Rank ? rank : lowestRank;
            }

            return lowestRank;
        }

        public async Task<List<TRank>> GetRankingsAsync(string ISO3)
        {
            List<TRank> allRanks = new();
            List<TRank> countryRanks = new();
            

            foreach (KeyValuePair<string, Dictionary<string, string>> sport in Urls)
            {
                Sport = sport.Key;

                foreach (KeyValuePair<string, string> gender in sport.Value)
                {
                    Gender = gender.Key;

                    TRank unranked = new()
                    {
                        IOC = CountryUtil.GetIOCMapping(ISO3),
                        Sport = Sport,
                        Gender = Gender,
                        Rank = 200
                    };

                    string? html = await CallUrl(gender.Value);
                    HtmlDocument? htmlDocument = new();
                    htmlDocument.LoadHtml(html);

                    allRanks = ParseRanks(htmlDocument);
                    TRank countryRank = allRanks.FirstOrDefault(x => x.IOC == CountryUtil.GetIOCMapping(ISO3), unranked);
                    countryRanks.Add(countryRank);
                }
            }          

            return countryRanks;
        }

        private static async Task<string> CallUrl(string fullUrl)
        {
            HttpClient? httpClient = new();
            var response = string.Empty;

            if (fullUrl.StartsWith("wwwroot"))
            {
                response = File.ReadAllText(fullUrl);
            }
            else
            {
                response = await httpClient.GetStringAsync(fullUrl);
            }

            return response;
        }

        protected virtual List<TRank> ParseRanks(HtmlDocument htmlDoc)
        {
            return new List<TRank>();
        }

        private static Dictionary<string, Dictionary<string, string>> MapUrls()
        {
            string urlsConfig = GeneralUtil.ReadConfig("urls");
            var urlsDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(urlsConfig);
            var sportsDict = urlsDict[typeof(TRank).Name];

            return sportsDict;
        }
    }
}
