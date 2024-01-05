using SportsRankingService.Interfaces;

namespace SportsRankingService.Repository
{
    public interface IWorldRankRepository
    {
        Task AddRangeAsync(IEnumerable<IRanking> rankings);
    }
}