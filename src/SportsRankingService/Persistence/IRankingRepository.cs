using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

public enum SaveOutcome
{
    /// <summary>The content differed from the feed's newest release (or there was none), so a new release was written.</summary>
    Inserted,

    /// <summary>Same content as the feed's newest release; only its LastSeenAt moved.</summary>
    Unchanged,
}

public interface IRankingRepository
{
    Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken);
}
