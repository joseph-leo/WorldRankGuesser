using Microsoft.EntityFrameworkCore;
using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

/// <summary>Insert-on-change: compares the snapshot's hash with the feed's newest release and only writes when it differs.</summary>
public sealed class RankingRepository(RankingsDbContext db, TimeProvider clock) : IRankingRepository
{
    public async Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken)
    {
        byte[] hash = RankingContentHash.Compute(snapshot);
        DateTimeOffset now = clock.GetUtcNow();

        // Greatest Id is newest; identity order is insertion order and it works on every provider.
        RankingRelease? newest = await db.Releases
            .Where(r => r.Sport == snapshot.Sport && r.Event == snapshot.Event && r.Gender == snapshot.Gender)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is not null && newest.ContentHash.AsSpan().SequenceEqual(hash))
        {
            newest.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Unchanged;
        }

        RankingRelease release = new()
        {
            Sport = snapshot.Sport,
            Event = snapshot.Event,
            Gender = snapshot.Gender,
            RankingDate = snapshot.RankingDate,
            IsFederationDate = snapshot.IsFederationDate,
            ContentHash = hash,
            FirstSeenAt = now,
            LastSeenAt = now,
            Rows = snapshot.Entries
                .Select((entry, ordinal) => new RankingRow
                {
                    Ordinal = ordinal,
                    Position = entry.Position,
                    ISO3 = entry.ISO3,
                    TeamName = entry.TeamName,
                    Competitor = entry.Competitor,
                    Points = entry.Points,
                })
                .ToList(),
        };

        db.Releases.Add(release);
        await db.SaveChangesAsync(cancellationToken);
        return SaveOutcome.Inserted;
    }
}
