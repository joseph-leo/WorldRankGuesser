using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers;

/// <summary>
/// BWF ranking table API (vue-rankingtable). Countries come as names only. Doubles rows carry
/// two players and yield one entry per partner with the pair's position and points; each entry
/// names its own partner. Players listed as "Athlete Independent Neutral" have no country and
/// are dropped. The full lists echo some rows inside a block of tied players (a new row id, the same
/// player ids, rank and points); a player or pair is one entry however often it is listed, and an echo
/// that disagrees on rank or points fails the parse. A player ranked with several partners has an entry
/// per pair, except that two of his pairs tied on rank and points list him once: an entry names the
/// player, not the pair, so the two would be identical (decided 2026-09-18). Player names arrive as HTML ("&lt;span class="name-1"&gt;Jonatan&lt;/span&gt; ...").
/// </summary>
public sealed partial class BwfParser : JsonRankingParser<BwfParser.Root>
{
    public override string SourceName => "Bwf";

    public sealed record Root(Results Results);
    public sealed record Results(List<Row> Data, int? Total, [property: JsonPropertyName("last_page")] int? LastPage);
    public sealed record Row(
        short Rank,
        decimal? Points,
        [property: JsonPropertyName("player1_id")] int? Player1Id,
        [property: JsonPropertyName("player2_id")] int? Player2Id,
        [property: JsonPropertyName("player1_model")] Player? Player1,
        [property: JsonPropertyName("player2_model")] Player? Player2,
        [property: JsonPropertyName("p1_country_model")] Country? Player1Country,
        [property: JsonPropertyName("p2_country_model")] Country? Player2Country);
    public sealed record Player([property: JsonPropertyName("name_display_bold")] string? NameHtml);
    public sealed record Country(string? Name);

    protected override IEnumerable<RankEntry> Map(Root root)
    {
        Dictionary<(int, int?), Row> listed = [];
        HashSet<(int PlayerId, short Rank, decimal? Points)> placed = [];

        foreach (Row row in root.Results.Data)
        {
            if (row.Player1Id is int player1Id && !listed.TryAdd((player1Id, row.Player2Id), row))
            {
                Row first = listed[(player1Id, row.Player2Id)];
                if (first.Rank != row.Rank || first.Points != row.Points)
                {
                    throw new ParseException(SourceName,
                        $"player ids {player1Id}/{row.Player2Id} are listed at rank {first.Rank} with {first.Points} points and again at rank {row.Rank} with {row.Points}");
                }

                continue;
            }

            foreach ((int? playerId, Player? player, Country? country) in new[] { (row.Player1Id, row.Player1, row.Player1Country), (row.Player2Id, row.Player2, row.Player2Country) })
            {
                if (country?.Name is null || IsNeutral(country.Name))
                {
                    continue;
                }

                // Two of a player's pairs tied on rank and points would give him two identical entries.
                if (playerId is int id && !placed.Add((id, row.Rank, row.Points)))
                {
                    continue;
                }

                if (!CountryUtil.TryGetISO3FromCountry(country.Name, out string? iso3))
                {
                    throw new ParseException(SourceName, $"no ISO3 mapping for country name '{country.Name}'");
                }

                yield return new RankEntry(row.Rank, iso3!, CleanName(player?.NameHtml), row.Points);
            }
        }
    }

    /// <summary>Strips the name's HTML markup and collapses whitespace; null when the feed gives none.</summary>
    private static string? CleanName(string? nameHtml)
    {
        if (nameHtml is null)
        {
            return null;
        }

        string name = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(nameHtml, " ")), " ").Trim();
        return name.Length == 0 ? null : name;
    }

    private static bool IsNeutral(string countryName) =>
        countryName.Contains("Neutral", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
