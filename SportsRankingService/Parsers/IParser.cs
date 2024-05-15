using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Parsers
{
    public interface IParser
    {
        public abstract IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItems);
    }
}
