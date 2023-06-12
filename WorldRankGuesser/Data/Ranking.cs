using System.Text.Json.Serialization;

namespace WorldRankGuesser.Data
{
    public class Ranking
    {
        public virtual string? IOC { get; set; }

        public virtual int Rank { get; set; }
    }
}
