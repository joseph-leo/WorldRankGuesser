using SportsRankingService.Models;

namespace SportsRankingService.Repository
{
    public interface IWorldRankRepository
    {
        Task AddRangeAsync(IEnumerable<IRanking> rankings);
    }
}