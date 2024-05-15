using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Net;

namespace SportsRankingService.Parsers
{
    public class GymnasticsParser(ILogger<GymnasticsParser> logger) : IParser
    {
        private readonly ILogger<GymnasticsParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);
                var tables = htmlDocument.DocumentNode.SelectNodes("//table").NotNullOrEmpty();

                foreach (var table in tables)
                {
                    HtmlNodeCollection headerRows = table.SelectNodes("thead/tr").NotNullOrEmpty();

                    rankingItem.Event = headerRows[0].InnerText.Trim().Split('(')[0].Trim();

                    HtmlNodeCollection rows = table.SelectNodes("tbody/tr").NotNullOrEmpty();

                    foreach (var row in rows)
                    {
                        List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                        string countryCode = string.Empty;
                        string? countryName = string.Empty;

                        if (cells[2].Contains(';'))
                        {
                            countryCode = cells[2].Split(';')[1];
                            countryCode = countryCode.Trim().IOCToISO3();
                            countryName = CountryUtil.GetCountryName(countryCode);
                        }
                        else
                        {
                            countryName = cells[2];
                            countryCode = CountryUtil.GetISO3FromCountry(countryName);
                        }
                        short position = short.Parse(cells[0]);

                        SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, countryCode);

                        rankings.Add(sportsRanking);
                    }
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
