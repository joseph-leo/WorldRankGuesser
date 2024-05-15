using SportsRankingService.Enums;
using SportsRankingService.Services;

namespace SportsRankingService.Factories
{
    public interface IScrapeServiceFactory
    {
        IScrapeService Create(RankingType rankingType);
    }
}