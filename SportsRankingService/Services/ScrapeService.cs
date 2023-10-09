using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace SportsRankingService.Services
{
    public abstract class ScrapeService
    {
        protected virtual Dictionary<string, Dictionary<string, string>> Urls { get => MapUrls(); }
        protected string? Sport { get; set; }
        protected string? Gender { get; set; }

        protected readonly ILogger<ScrapeService> _logger;
        private readonly ConfigHelper _config;

        public ScrapeService(ILogger<ScrapeService> logger)
        {
            _logger = logger;
            _config = new("serviceconfig");
        }

        public async Task<List<SportsRanking>> GetSportRanksAsync()
        {
            List<SportsRanking> allRanks = new();

            foreach (KeyValuePair<string, Dictionary<string, string>> sport in Urls)
            {
                //sport.Key is Sport, sport.Value is Gender/URL pair
                Sport = sport.Key;

                foreach (KeyValuePair<string, string> gender in sport.Value)
                {
                    //gender.Key is Gender, gender.Value is URL
                    Gender = gender.Key;

                    string? response = await CallUrlAsync(gender.Value);
                    allRanks.AddRange(ParseRanks(response));
                }
            }

            return allRanks;
        }

        protected abstract List<SportsRanking> ParseRanks(string response);

        protected virtual async Task<string> CallUrlAsync(string fullUrl)
        {
            HttpClient? httpClient = new();
            string? response;

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

        protected static void Log(Exception ex, ILogger logger)
        {
            string InnerMessage = ex.InnerException is null ? string.Empty : ex.InnerException.Message;

            logger.LogError("{StackTrace} | Source=({Source}) | Exception Message: '{Message}' | Inner Exception Message: '{InnerException}'", ex.StackTrace, ex.Source, ex.Message, InnerMessage);
        }

        

        private Dictionary<string, Dictionary<string, string>> MapUrls()
        {
            var urlsDict = _config.Urls;
            var sportsDict = urlsDict[GetType().Name];

            return sportsDict;
        }
    }
}
