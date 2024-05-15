using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Models
{
    public interface IRanking
    {
        short Position { get; set; }
        string? TeamName { get; set; }
        string? ISO3 { get; set; }
        string? Sport { get; set; }
        string? Gender { get; set; }
    }
}
