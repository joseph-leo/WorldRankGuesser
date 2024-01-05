using HtmlAgilityPack;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Net;

namespace SportsRankingService.Services.World
{
    public class GymnasticsService : WorldRankService
    {
        public GymnasticsService(ILogger<GymnasticsService> logger) : base(logger)
        {
        }

        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            try
            {
                List<SportsRanking> ranks = [];

                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);
                var tables = htmlDocument.DocumentNode.SelectNodes("//table").NotNullOrEmpty();

                foreach (var table in tables)
                {
                    HtmlNodeCollection headerRows = table.SelectNodes("thead/tr").NotNullOrEmpty();

                    rankInfo.Event = headerRows[0].InnerText.Trim().Split('(')[0].Trim();

                    HtmlNodeCollection rows = table.SelectNodes("tbody/tr").NotNullOrEmpty();

                    foreach (var row in rows)
                    {
                        List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                        string countryCode = string.Empty;
                        string? countryName = string.Empty;

                        if (cells[2].Contains(';'))
                        {
                            countryCode = cells[2].Split(';')[1];
                            countryCode = countryCode.IOCToISO3();
                            countryName = CountryUtil.GetCountryName(countryCode);
                        }
                        else
                        {
                            countryName = cells[2];
                            countryCode = CountryUtil.GetISO3FromCountry(countryName);
                        }
                        short position = short.Parse(cells[0]);

                        ranks.Add(new SportsRanking
                        {
                            Event = rankInfo.Event,
                            Sport = rankInfo.Sport,
                            Gender = rankInfo.Gender,
                            ISO3 = countryCode,
                            Position = position,
                            //CountryName = countryName
                        });
                    }
                }

                return ranks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
                return [];
            }
        }
    }
}
