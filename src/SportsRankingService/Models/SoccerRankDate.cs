namespace SportsRankingService.Models;

/// <summary>One entry of FIFA's ranking-schedule list inside the page's __NEXT_DATA__; Newtonsoft binds it by property name.</summary>
public class SoccerRankDate
{
    public required string id { get; set; }

    public required string iso { get; set; }

    public required string dateText { get; set; }
}
