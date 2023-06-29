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
    public abstract class ScrapeService<TRank, TRankModel> where TRank : Rank, new()
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
                lowestRank = rank.Position < lowestRank.Position ? rank : lowestRank;
            }

            return lowestRank;
        }

        private async Task<List<TRank>> GetRankingsAsync(string ISO3)
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
                        ISO3 = ISO3,
                        Sport = Sport,
                        Gender = Gender,
                        Position = 200
                    };

                    string? response = await CallUrlAsync(gender.Value);
                    allRanks = ParseRanks(response);

                    List<TRank> countryRank = GetCountryRank(allRanks, ISO3);

                    if (countryRank.Any()) 
                    {
                        countryRanks.AddRange(countryRank);
                    }
                    else
                    {
                        countryRanks.Add(unranked);
                    }                   
                }
            }          

            return countryRanks;
        }
        protected virtual List<TRank> GetCountryRank(List<TRank> allRanks, string ISO3) 
        {
            return allRanks.Where(x => x.ISO3 == ISO3).ToList();
        }

        protected virtual async Task<string> CallUrlAsync(string fullUrl)
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

        protected abstract List<TRank> ParseRanks(string response);

        private static Dictionary<string, Dictionary<string, string>> MapUrls()
        {
            string urlsConfig = GeneralUtil.ReadConfig("urls");
            var urlsDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(urlsConfig);
            var sportsDict = urlsDict[typeof(TRank).Name];

            return sportsDict;
        }

        public async Task<List<TRank>> GetAllRanksAsync()
        {
            List<TRank> allRanks = new();

            foreach (KeyValuePair<string, Dictionary<string, string>> sport in Urls)
            {
                Sport = sport.Key;

                foreach (KeyValuePair<string, string> gender in sport.Value)
                {
                    Gender = gender.Key;

                    string? response = await CallUrlAsync(gender.Value);
                    allRanks.AddRange(ParseRanks(response));
                }
            }

            return allRanks;
        }
    }
}
