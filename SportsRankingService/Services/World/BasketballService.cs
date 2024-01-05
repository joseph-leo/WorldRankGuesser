using HtmlAgilityPack;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;


namespace SportsRankingService.Services.World
{
    public class BasketballService(ILogger<BasketballService> logger) : WorldRankService(logger)
    {
        public override IEnumerable<IRanking> ParseResponse(string response, IRanking prototype)
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

                    short position = short.Parse(cells[0].Trim('.'));
                    string _ISO3 = cells[3].IOCToISO3();

                    rankings.Add(new SportsRanking
                    {
                        ISO3 = _ISO3,
                        Position = position,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        Gender = rankInfo.Gender,
                        RankDate = DateTime.Now,
                        //CountryName = CountryUtil.GetCountryName(_ISO3)
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
