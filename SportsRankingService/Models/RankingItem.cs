using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Models
{
    public class RankingItem
    {
        public string? Gender { get; set; }
        public string? Sport { get; set; }
        public string Url { get; set; }
        public string? Event { get; set; }
    }
}
