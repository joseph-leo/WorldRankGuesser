using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

/// <summary>
/// Identity of a release's content: SHA-256 over a header line (the ranking date when it is the
/// federation's, otherwise empty) and one "position, ISO3, team name, competitor, points"
/// tab-separated line per entry in order, so any stored fact changing makes a new release.
/// The feed identity is not included because the repository compares within one feed only.
/// </summary>
public static class RankingContentHash
{
    public static byte[] Compute(RankingSnapshot snapshot)
    {
        StringBuilder text = new();

        text.Append(snapshot.IsFederationDate ? snapshot.RankingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "").Append('\n');

        foreach (RankingSnapshotEntry entry in snapshot.Entries)
        {
            text.Append(entry.Position.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(entry.ISO3).Append('\t')
                .Append(entry.TeamName).Append('\t')
                .Append(entry.Competitor).Append('\t')
                .Append(entry.Points?.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
