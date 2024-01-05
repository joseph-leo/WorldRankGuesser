using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Linq;
using System.Reflection;

namespace SportsRankingService.Services.World
{
    public class HockeyService(ILogger<HockeyService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            return rankInfo.Sport switch
            {
                "Field Hockey" => ParseFieldHockey(response, rankInfo),
                "Ice Hockey" => ParseIceHockey(response, rankInfo.Sport),
                _ => [],
            };
        }

        private List<SportsRanking> ParseFieldHockey(string response, WorldRankInfo rankInfo)
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

                    rankings.Add(new SportsRanking
                    {
                        ISO3 = ISO3,
                        Position = position,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        Gender = rankInfo.Gender
                    });
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
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
                    string ISO3 = countryCode.IOCToISO3();

                    rankings.Add(new SportsRanking
                    {
                        ISO3 = ISO3,
                        Position = position,
                        Sport = sport,
                        Gender = gender
                    });
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
