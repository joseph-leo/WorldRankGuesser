using SportsRankingService.Enums;

namespace SportsRankingService.Services
{
    public interface IRankingUpdater
    {
        Task UpdateRankingsAsync(RankingType rankingType);
        Task UpdateWorldRankAsync(WorldSports sport);
    }
}