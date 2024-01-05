using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Services.American
{
    public abstract class AmericanRankService : IScrapeService<AmRankInfo>
    {
        public async Task<IEnumerable<IRanking>> GetSportRanksAsync()
        {
            throw new NotImplementedException();
        }
        public abstract IEnumerable<IRanking> ParseResponse(string response, AmRankInfo rankInfo);

        public async Task<string> CallUrlAsync(string url)
        {
            throw new NotImplementedException();
        }
    }
}
