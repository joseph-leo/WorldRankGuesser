using HtmlAgilityPack;
using SportsRankingService.Utilities;
using SportsRankingService.Models;

namespace SportsRankingService.Parsers
{
    public class BaseballParser(ILogger<BaseballParser> logger) : IParser
    {
        private readonly ILogger<BaseballParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
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
                    string _ISO3 = cells[2].Trim().IOCToISO3();

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
