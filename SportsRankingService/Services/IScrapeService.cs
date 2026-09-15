using SportsRankingService.Enums;
using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Services
{
    public interface IScrapeService
    {
        public Task<IEnumerable<IRanking>> GetSportRanksAsync(WorldSports sport);
        public Task<string?> CallUrlAsync(string url);

    }
}
