using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportsRankingService.Models;

public partial class SportsRanking
{
    [Key]
    public int ID { get; set; }

    [StringLength(3)]
    public string? ISO3 { get; set; }

    [StringLength(50)]
    public string? CountryName { get; set; }

    [StringLength(50)]
    public string? Sport { get; set; }

    [StringLength(50)]
    public string? Event { get; set; }

    [StringLength(10)]
    public string? Gender { get; set; }

    public short? Position { get; set; }

    [Column(TypeName = "date")]
    public DateTime? RankDate { get; set; }
}
