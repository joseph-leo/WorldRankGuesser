using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;


namespace SportsRankingService.Parsers
{
    public class BasketballParser(ILogger<BasketballParser> logger) : IParser
    {
        private readonly ILogger<BasketballParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlDocument htmlDocument = new();
                htmlDocument.LoadHtml(response);
                HtmlNode table = htmlDocument.DocumentNode.SelectSingleNode("//table");
                HtmlNodeCollection rows = table.SelectNodes("tbody/tr").NotNullOrEmpty();

                foreach (var row in rows)
                {
                    List<string> cells = row.SelectNodes("td").NotNullOrEmpty().Select(x => x.InnerText.Trim()).ToList();

                    short position = short.Parse(cells[0].Trim('.'));
                    string _ISO3 = cells[3].Trim().IOCToISO3();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, _ISO3);

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
    }
}
