using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Interfaces
{
    public interface IScrapeService
    {
        public Task<IEnumerable<IRanking>> GetSportRanksAsync();
        public Task<string> CallUrlAsync(string url);

        public IEnumerable<IRanking> ParseResponse(string response, IRanking rankInfo);
    }
}
