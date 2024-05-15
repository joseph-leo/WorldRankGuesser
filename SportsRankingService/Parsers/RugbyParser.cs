using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Diagnostics;

namespace SportsRankingService.Parsers
{
    public class RugbyParser(ILogger<RugbyParser> logger) : IParser
    {
        private readonly ILogger<RugbyParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            return rankingItem.Event switch
            {
                "Union" => ParseUnion(response, rankingItem),
                "Sevens" => ParseSevens(response, rankingItem),
                _ => []
            };
        }

        private List<SportsRanking> ParseUnion(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);

                JToken entries = json["entries"].NotNullOrEmpty();

                foreach (JToken result in entries)
                {
                    JToken team = result["team"].NotNullOrEmpty();

                    short position = result["pos"].NotNullOrEmpty().Value<short>();
                    string countryCode = team["countryCode"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, countryCode);

                    rankings.Add(sportsRanking);
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return [];
            }

        }

        private List<SportsRanking> ParseSevens(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlDocument htmlDocument = new();
                htmlDocument.LoadHtml(response);

                HtmlNodeCollection tables = htmlDocument.DocumentNode.SelectNodes("//table");
                HtmlNode womensTable = tables[0];
                HtmlNode mensTable = tables[1];

                List<SportsRanking> womensRankings = ParseSevensTable(womensTable, "Women");
                rankings.AddRange(womensRankings);

                List<SportsRanking> mensRankings = ParseSevensTable(mensTable, "Men");
                rankings.AddRange(mensRankings);
                
                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
                return [];
            }
        }

        private List<SportsRanking> ParseSevensTable(HtmlNode table, string gender)
        {
            List<SportsRanking> rankings = [];

            HtmlNodeCollection rows = table.SelectNodes("tbody/tr");
            foreach (var row in rows)
            {
                List<string> cells = row.SelectNodes("td").NotNullOrEmpty().Select(x => x.InnerText.Trim()).ToList();
                string countryCode = cells[1].Trim().IOCToISO3();
                short position = short.Parse(cells[0]);

                SportsRanking sportsRanking = new(gender, "Sevens", "Rugby", position, countryCode);

                rankings.Add(sportsRanking);
            }

            return rankings;
        }
    }
}
