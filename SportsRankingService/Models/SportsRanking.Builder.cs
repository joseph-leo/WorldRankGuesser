using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Interfaces;

namespace SportsRankingService.Models;

public partial class SportsRanking : IRanking
{
    public SportsRanking(string gender, string _event, string sport)
    {
        Gender = gender;
        Event = _event;
        Sport = sport;
    }
    public IRanking ShallowCopy()
    {
        return (SportsRanking)MemberwiseClone();
    }

    public void AddRemaingProps(short position, string teamCode)
    {
        Position = position;
        ISO3 = teamCode;
    }
}
