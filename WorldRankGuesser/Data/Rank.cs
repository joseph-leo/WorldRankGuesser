using System.Text.Json.Serialization;

namespace WorldRankGuesser.Data
{
    public class Rank
    {
        public string? ISO3 { get; set; }
        public int Position { get; set; }
        public string? Sport { get; set; }
        public string? Gender { get; set; }
        public string? Flag { get; set; }
        public bool Unranked { get => Position == 200; }

    }
}
