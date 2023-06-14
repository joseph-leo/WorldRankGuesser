using System.Text.Json.Serialization;

namespace WorldRankGuesser.Data
{
    public class Ranking
    {
        public virtual string? IOC { get; set; }
        public virtual int Rank { get; set; }
        public virtual string? Sport { get; set; }
        public virtual string? Gender { get; set; }
        public bool Unranked { get => Rank == 200; }

    }
}
