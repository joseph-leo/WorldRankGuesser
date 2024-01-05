using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Diagnostics;

namespace SportsRankingService.Services.World
{
    public class RugbyService(ILogger<RugbyService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            return rankInfo.Event switch
            {
                "Union" => ParseUnion(response, rankInfo),
                "Sevens" => ParseSevens(response, rankInfo),
                _ => []
            };
        }

        private List<SportsRanking> ParseUnion(string response, WorldRankInfo rankInfo)
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
                    string countryName = team["name"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();

                    rankings.Add(new SportsRanking
                    {
                        Gender = rankInfo.Gender,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        ISO3 = countryCode.IOCToISO3(),
                        Position = position
                    });
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return [];
            }

        }

        private List<SportsRanking> ParseSevens(string response, WorldRankInfo rankInfo)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlDocument htmlDocument = new();
                htmlDocument.LoadHtml(response);

                HtmlNodeCollection rows = htmlDocument.DocumentNode.SelectNodes("//table/tbody/tr").NotNullOrEmpty();

                foreach (var row in rows)
                {
                    List<string> cells = row.SelectNodes("td").NotNullOrEmpty().Select(x => x.InnerText.Trim()).ToList();
                    string countryCode = cells[1].IOCToISO3();
                    short rank = short.Parse(cells[0]);

                    rankings.Add(new SportsRanking
                    {
                        ISO3 = countryCode,
                        Position = rank,
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
    }
}
