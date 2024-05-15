using SportsRankingService.Enums;

namespace SportsRankingService.Services
{
    public interface IRankingUpdater
    {
        Task UpdateRankingsAsync(RankingType rankingType, CancellationToken stoppingToken);
        Task UpdateWorldRankAsync(WorldSports sport, CancellationToken stoppingToken);
    }
}