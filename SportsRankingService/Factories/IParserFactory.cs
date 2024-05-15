using SportsRankingService.Enums;
using SportsRankingService.Parsers;

namespace SportsRankingService.Factories
{
    public interface IParserFactory
    {
        IParser Create(WorldSports sport);
    }
}