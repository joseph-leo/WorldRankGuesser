using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Reflection;

namespace SportsRankingService.Services.World
{
    public class TennisService(ILogger<TennisService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            return rankInfo.Event switch
            {
                "Singles" => ParseSingles(response, rankInfo),
                "Doubles" => ParseDoubles(response, rankInfo),
                _ => []
            };
        }

        private List<SportsRanking> ParseSingles(string response, WorldRankInfo rankInfo)
        {
            List<SportsRanking> rankings = [];

            try
            {
                JObject json = JObject.Parse(response);
                JToken standings = json["rankings"].NotNullOrEmpty();
                JToken ranks = standings[0].NotNullOrEmpty()["ranks"].NotNullOrEmpty();

                foreach (JToken entry in ranks)
                {
                    short position = entry["current"].NotNullOrEmpty().Value<short>();

                    JToken athlete = entry["athlete"].NotNullOrEmpty();
                    string countryCode = athlete["citizenshipCountry"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    countryCode = countryCode.ToUpper().IOCToISO3();

                    rankings.Add(new SportsRanking()
                    {
                        Position = position,
                        ISO3 = countryCode,
                        Event = rankInfo.Event,
                        Sport = rankInfo.Sport,
                        Gender = rankInfo.Gender,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
            }

            return rankings;
        }

        private List<SportsRanking> ParseDoubles(string response, WorldRankInfo rankInfo)
        {
            return rankInfo.Gender switch
            {
                "Men" => ParseMensDoubles(response, rankInfo),
                "Women" => ParseWomensDoubles(response, rankInfo),
                _ => []
            };
        }

        private List<SportsRanking> ParseMensDoubles(string response, WorldRankInfo rankInfo)
        {
            List<SportsRanking> rankings = [];

            try
            {
                HtmlDocument html = new();
                html.LoadHtml(response);

                List<HtmlNode> rows = html.DocumentNode.SelectNodes("//table/tbody/tr").Where(x => x.HasAttributes).ToList();

                foreach (HtmlNode row in rows)
                {
                    HtmlNodeCollection cells = row.SelectNodes("td");
                    string posText = cells[0].InnerText.Trim();

                    bool position = short.TryParse(cells[0].InnerText.Trim(), out short pos);

                    HtmlNode flagImage = cells[1].SelectSingleNode("ul/li[@class=\"avatar\"]").SelectSingleNode("img[@class=\"flag \"]");
                    string uri = flagImage.Attributes.AttributesWithName("src").First().NotNullOrEmpty().Value;
                    string countryCode = uri.Split('/').Last().Split('.')[0].ToUpper();
                    countryCode = countryCode.IOCToISO3();

                    rankings.Add(new SportsRanking()
                    {
                        Position = 1,
                        ISO3 = countryCode,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        Gender = rankInfo.Gender
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
            }

            return rankings;
        }

        private List<SportsRanking> ParseWomensDoubles(string response, WorldRankInfo rankInfo)
        {
            List<SportsRanking> rankings = [];

            try
            {
                JArray standings = JArray.Parse(response);

                foreach (var entry in standings)
                {
                    short position = entry["ranking"].NotNullOrEmpty().Value<short>();
                    JToken player = entry["player"].NotNullOrEmpty();
                    string countryCode = player["countryCode"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    countryCode = countryCode.IOCToISO3();

                    rankings.Add(new SportsRanking()
                    {
                        Position = position,
                        ISO3 = countryCode,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        Gender = rankInfo.Gender
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
            }

            return rankings;
        }
    }
}
