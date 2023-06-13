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

        protected override List<BaseballRank> ParseData(HtmlDocument htmlDoc)
        {
            List<HtmlNode> ulist = htmlDoc.DocumentNode.SelectNodes("//div").Where(x => x.GetClasses().Contains("ranking-listing-team-row")).ToList();
            List<BaseballRank> rankings = new();

            foreach (HtmlNode row in ulist)
            {
                List<string> cells = row.SelectNodes("ul/li").Select(x => x.InnerText.Trim()).ToList();

                if (!rankings.Any(x => x.ISO3 == cells[2]) && int.TryParse(cells[0], out int rank))
                {
                    rankings.Add(new BaseballRank
                    {
                        ISO3 = cells[2],
                        Rank = rank
                    });
                }
            }

            return rankings;
        }
    }
}
