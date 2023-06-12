using HtmlAgilityPack;
using System.Text.Json;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class ScrapeService<TRank, TRankModel> where TRank : Ranking, new()
    {
        protected virtual List<string>? Urls { get; set; }

        public async Task<TRank> GetLowestRankAsync(string ISO3)
        {
            TRank lowestRank = new()
            {
                IOC = ISO3,
                Rank = 200
            };

            foreach (string url in Urls)
            {
                TRank currentRank = await GetRankingAsync(url, ISO3);

                if (currentRank.Rank < lowestRank.Rank)
                {
                    lowestRank = currentRank;
                }
            }

            return lowestRank;
        }

        public async Task<TRank> GetRankingAsync(string fullUrl, string ISO3)
        {
            TRank? countryRank = new();

            string? html = await CallUrl(fullUrl);

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(html);
 
            List<TRank> ranks = ParseData(htmlDocument);

            countryRank = ranks.FirstOrDefault(x => x.IOC == CountryUtilities.GetIOCMapping(ISO3));

            if (countryRank == null)
            {
                countryRank = new TRank
                {
                    IOC = ISO3,
                    Rank = 200
                };
            }

            return countryRank;
        }

        private async Task<string> CallUrl(string fullUrl)
        {
            HttpClient? httpClient = new();
            var response = await httpClient.GetStringAsync(fullUrl);

            return response;
        }

        protected virtual List<TRank> ParseData(HtmlDocument htmlDoc)
        {
            return new List<TRank>();
        }
    }
}
