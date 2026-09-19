using Microsoft.EntityFrameworkCore;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Players;

public sealed class PlayerService(GameDbContext db, TimeProvider time)
{
    public async Task<Player> CreateAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var player = new Player { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };

        db.Players.Add(player);
        await db.SaveChangesAsync(ct);

        return player;
    }

    /// <summary>False when the player no longer exists (for example a cookie that outlived a database reset).</summary>
    public async Task<bool> TouchAsync(Guid playerId, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        return await db.Players
            .Where(p => p.Id == playerId)
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.LastSeenAt, now), ct) == 1;
    }
}
