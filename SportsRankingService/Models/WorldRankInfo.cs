using SportsRankingService.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Models
{
    public class WorldRankInfo(string sport, string _event, string gender) : ISportInfo
    {
        public string Sport { get; set; } = sport;
        public string Gender { get; set; } = gender;
        public string Event { get; set; } = _event;
    }
}
