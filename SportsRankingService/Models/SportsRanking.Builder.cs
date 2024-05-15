using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportsRankingService.Models;

public partial class SportsRanking : IRanking
{
    public SportsRanking() { }

    public SportsRanking(string? gender, string? _event, string? sport, short position, string Iso3)
    {
        Gender = gender;
        Event = _event;
        Sport = sport;
        Position = position;
        ISO3 = Iso3;
    }
}
