using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Reflection;

namespace SportsRankingService.Parsers
{
    public class TennisParser(ILogger<TennisParser> logger) : IParser
    {
        private readonly ILogger<TennisParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            return rankingItem.Event switch
            {
                "Singles" => ParseSingles(response, rankingItem),
                "Doubles" => ParseDoubles(response, rankingItem),
                _ => []
            };
        }

        private List<SportsRanking> ParseSingles(string response, RankingItem rankingItem)
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

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, countryCode);

                    rankings.Add(sportsRanking);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
            }

            return rankings;
        }

        private List<SportsRanking> ParseDoubles(string response, RankingItem rankingItem)
        {
            return rankingItem.Gender switch
            {
                "Men" => ParseMensDoubles(response, rankingItem),
                "Women" => ParseWomensDoubles(response, rankingItem),
                _ => []
            };
        }

        private List<SportsRanking> ParseMensDoubles(string response, RankingItem rankingItem)
        {
            List<SportsRanking> rankings = [];

            try
            {
                HtmlDocument html = new();
                html.LoadHtml(response);

                IEnumerable<HtmlNode> rows = html.DocumentNode.SelectNodes("//table/tbody/tr").NotNullOrEmpty().Where(x => x.HasAttributes);

                foreach (HtmlNode row in rows)
                {
                    HtmlNodeCollection cells = row.SelectNodes("td");

                    string posText = cells[0].InnerText.Trim().Split('T').First();

                    short position = short.Parse(posText);

                    HtmlNode flagNode = cells[1].SelectSingleNode("ul/li[@class=\"avatar\"]").NotNullOrEmpty();

                    HtmlNode flagImage = flagNode.SelectSingleNode("img[contains(concat(' ',normalize-space(@class),' '),' flag ')]").NotNullOrEmpty();

                    string uri = flagImage.Attributes.AttributesWithName("src").FirstOrDefault().NotNullOrEmpty().Value;

                    string countryCode = uri.Split('/').Last().Split('.').First().ToUpper();
                    countryCode = countryCode.IOCToISO3();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, countryCode);

                    rankings.Add(sportsRanking);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
            }

            return rankings;
        }

        private List<SportsRanking> ParseWomensDoubles(string response, RankingItem rankingItem)
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
                    countryCode = countryCode.Trim().IOCToISO3();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, countryCode);

                    rankings.Add(sportsRanking);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
            }

            return rankings;
        }
    }
}
