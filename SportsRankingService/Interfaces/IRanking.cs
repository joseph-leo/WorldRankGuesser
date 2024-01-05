using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Interfaces
{
    public interface IRanking
    {
        short Position { get; set; }
        string? TeamName { get; set; }
        string? Sport { get; set; }
        string? Gender { get; set; }
        IRanking ShallowCopy();
        void AddRemaingProps(short position, string teamCode);
    }
}
