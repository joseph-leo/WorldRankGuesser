namespace SportsRankingService.Models;

public class WorldRankInfo(string sport, string _event, string gender)
{
    public string Sport { get; set; } = sport;
    public string Gender { get; set; } = gender;
    public string Event { get; set; } = _event;
}
