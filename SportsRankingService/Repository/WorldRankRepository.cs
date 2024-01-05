using SportsRankingService.Interfaces;
using SportsRankingService.RankingsDb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Repository
{
    public class WorldRankRepository(WorldRankGuesserContext context) : IWorldRankRepository
    {
        private readonly WorldRankGuesserContext _context = context;

        public async Task AddRangeAsync(IEnumerable<IRanking> rankings)
        {
            await _context.AddRangeAsync(rankings);
            await _context.SaveChangesAsync();
        }
    }
}
