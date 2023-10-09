using HtmlAgilityPack;
using SportsRankingService.Utilities;
using SportsRankingService.Models;

namespace SportsRankingService.Services
{
    public class BaseballService : ScrapeService
    {
        public BaseballService(ILogger<ScrapeService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);

                List<HtmlNode> ulist = htmlDocument.DocumentNode.SelectNodes("//div").Where(x => x.GetClasses().Contains("ranking-listing-team-row")).ToList();
                List<SportsRanking> rankings = new();

                foreach (HtmlNode row in ulist)
                {
                    List<string> cells = row.SelectNodes("ul/li").Select(x => x.InnerText.Trim()).ToList();

                    if (short.TryParse(cells[0], out short rank))
                    {
                        rankings.Add(new SportsRanking
                        {
                            ISO3 = CountryUtil.IOCToISO3(cells[2]),
                            Position = rank,
                            Sport = Sport,
                            Gender = Gender
                        });
                    }
                }

                return rankings;
            }
            catch (Exception ex)
            {
                Log(ex, _logger);
                return new List<SportsRanking>();
            }           
        }
    }
}
