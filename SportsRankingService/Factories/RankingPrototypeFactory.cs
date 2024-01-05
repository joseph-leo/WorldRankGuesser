using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public static class RankingPrototypeFactory
    {
        public static IRanking CreateWorldRanking(string gender, string _event, string sport)
        {
            return new SportsRanking(gender, _event, sport);
        }
    }
}
