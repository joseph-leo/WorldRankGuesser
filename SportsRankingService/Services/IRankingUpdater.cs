namespace SportsRankingService.Services;

/// <summary>Counts for one run. A feed that yielded nothing or threw is Failed; the others are Inserted or Unchanged.</summary>
public sealed record UpdateSummary(int Feeds, int Inserted, int Unchanged, int Failed);

public interface IRankingUpdater
{
    /// <summary>Fetches every enabled feed and saves its snapshot. One failing feed does not affect the others.</summary>
    Task<UpdateSummary> UpdateAllAsync(CancellationToken cancellationToken);
}
