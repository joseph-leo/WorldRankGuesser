using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>The only code that knows the rankings come from a view. Selecting every mapped column also proves the view still has them.</summary>
public sealed class RankingsReader(GameDbContext db) : IRankingsReader
{
    public async Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct) =>
        await db.CountryRankings.AsNoTracking().ToListAsync(ct);
}
