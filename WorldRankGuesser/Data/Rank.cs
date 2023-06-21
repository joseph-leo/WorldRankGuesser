using System.Text.Json.Serialization;

namespace WorldRankGuesser.Data
{
    public class Rank
    {
        public virtual string? IOC { get; set; }
        public virtual int Position { get; set; }
        public virtual string? Sport { get; set; }
        public virtual string? Gender { get; set; }
        public bool Unranked { get => Position == 200; }

    }
}
