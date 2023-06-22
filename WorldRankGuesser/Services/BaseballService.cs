using HtmlAgilityPack;
using System.Xml.XPath;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class BaseballService : ScrapeService<BaseballRank, BaseballRank>
    {
        //protected override List<string> Urls
        //{
        //    get
        //    {
        //        return new List<string>
        //        {
        //            "https://rankings.wbsc.org/list/baseball/men",
        //            "https://rankings.wbsc.org/list/softball/women"
        //        };
        //    }
        //}

        protected override List<BaseballRank> ParseRanks(string response)
        {
            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);

            List<HtmlNode> ulist = htmlDocument.DocumentNode.SelectNodes("//div").Where(x => x.GetClasses().Contains("ranking-listing-team-row")).ToList();
            List<BaseballRank> rankings = new();

            foreach (HtmlNode row in ulist)
            {
                List<string> cells = row.SelectNodes("ul/li").Select(x => x.InnerText.Trim()).ToList();

                if (int.TryParse(cells[0], out int rank))
                {
                    rankings.Add(new BaseballRank
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
    }
}
