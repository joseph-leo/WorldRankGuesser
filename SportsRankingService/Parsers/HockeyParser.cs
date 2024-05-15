using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Linq;
using System.Reflection;

namespace SportsRankingService.Parsers
{
    public class HockeyParser(ILogger<HockeyParser> logger) : IParser
    {
        private readonly ILogger<HockeyParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            return rankingItem.Sport switch
            {
                "Field Hockey" => ParseFieldHockey(response, rankingItem),
                "Ice Hockey" => ParseIceHockey(response, rankingItem.Sport),
                _ => [],
            };
        }

        private List<SportsRanking> ParseFieldHockey(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);
                JToken teams = json["ranks"].NotNullOrEmpty();

                foreach (JToken team in teams)
                {
                    string countryCode = team["team_short_code"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    short position = team["rank"].NotNullOrEmpty().Value<short>();

                    string ISO3 = countryCode.IOCToISO3();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, ISO3);

                    rankings.Add(sportsRanking);
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
                return [];
            }

        }

        private List<SportsRanking> ParseIceHockey(string response, string sport)
        {
            List<SportsRanking> rankings = [];

            HtmlDocument htmlDocument = new();
            htmlDocument.LoadHtml(response);
            var tables = htmlDocument.DocumentNode.SelectNodes("//table");

            HtmlNode mensTable = tables[0];
            rankings.AddRange(ParseIceHockeyTable(mensTable, sport, "Men"));

            HtmlNode womensTable = tables[1];
            rankings.AddRange(ParseIceHockeyTable(womensTable, sport, "Women"));

            return rankings;
        }

        private List<SportsRanking> ParseIceHockeyTable(HtmlNode table, string sport, string gender)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlNodeCollection rows = table.SelectNodes("tbody/tr").NotNullOrEmpty();

                foreach (var row in rows)
                {
                    List<string> cells = row.SelectNodes("td").NotNullOrEmpty().Select(x => x.InnerText.Trim()).ToList();
                    string countryCode = row.SelectSingleNode("td/a/span[@class=\"show-small\"]").InnerText;

                    short position = short.Parse(cells[0]);
                    string ISO3 = countryCode.Trim().IOCToISO3();

                    SportsRanking sportsRanking = new(sport, null, sport, position, ISO3);

                    rankings.Add(sportsRanking);
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", sport);
                return [];
            }
        }
    }
}
