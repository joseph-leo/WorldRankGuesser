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
        protected virtual Dictionary<string, Dictionary<string, string>>? Urls { get => MapUrls(); }

        public async Task<TRank> GetLowestRankAsync(string ISO3)
        {
            TRank lowestRank = new()
            {
                ISO3 = ISO3,
                Rank = 200
            };

            foreach (KeyValuePair<string, Dictionary<string, string>> sport in Urls)
            {
                foreach (KeyValuePair<string, string> gender in sport.Value)
                {
                    TRank currentRank = await GetRankingAsync(gender.Value, ISO3);

                    if (currentRank.Rank < lowestRank.Rank)
                    {
                        lowestRank = currentRank;
                        lowestRank.Sport = sport.Key;
                        lowestRank.Gender = gender.Key;
                    }
                }               
            }

            return lowestRank;
        }

        //TODO: Set Sport & Gender in ParseData
        public async Task<TRank> GetRankingAsync(string fullUrl, string ISO3)
        {
            TRank? countryRank = new();

            string? html = await ScrapeService<TRank, TRankModel>.CallUrl(fullUrl);

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(html);
 
            List<TRank> ranks = ParseData(htmlDocument);

            countryRank = ranks.FirstOrDefault(x => x.ISO3 == CountryUtilities.GetIOCMapping(ISO3));

            if (countryRank == null)
            {
                countryRank = new TRank
                {
                    ISO3 = ISO3,
                    Rank = 200
                };
            }

            return countryRank;
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

        protected virtual List<TRank> ParseData(HtmlDocument htmlDoc)
        {
            return new List<TRank>();
        }

        private static Dictionary<string, Dictionary<string, string>> MapUrls()
        {
            string urlsConfig = GeneralUtilities.ReadConfig("urls");
            var urlsDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(urlsConfig);
            var sportsDict = urlsDict[typeof(TRank).Name];

            return sportsDict;
        }
    }
}
