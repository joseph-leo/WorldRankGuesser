using HtmlAgilityPack;
using SportsRankingService.Utilities;
using SportsRankingService.Models;
using SportsRankingService.Interfaces;

namespace SportsRankingService.Services.World
{
    public class BaseballService(ILogger<BaseballService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            try
            {
                List<SportsRanking> rankings = [];

                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);

                HtmlNodeCollection ulist = htmlDocument.DocumentNode.SelectNodes("//div[@class=\"ranking-listing-team-row\"]").NotNullOrEmpty();

                foreach (HtmlNode row in ulist)
                {
                    HtmlNodeCollection listItems = row.SelectNodes("ul/li").NotNullOrEmpty();
                    List<string> cells = listItems.Select(x => x.InnerText.Trim()).ToList().NotNullOrEmpty();

                    short position = short.Parse(cells[0]);
                    string _ISO3 = cells[2].IOCToISO3();

                    rankings.Add(new SportsRanking
                    {
                        ISO3 = _ISO3,
                        Position = position,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        Gender = rankInfo.Gender,
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
