using SportsRankingService.Enums;
using SportsRankingService.Services.World;

namespace SportsRankingService.Factories
{
    public interface IWorldRankServiceFactory
    {
        WorldRankService Create(WorldSports sport);
    }
}