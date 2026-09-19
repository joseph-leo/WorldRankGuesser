using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Games;

public sealed class RankingsUnavailableException() : Exception("The rankings have not been loaded yet.");

public abstract record PickResult
{
    private PickResult() { }

    public sealed record Ok(GameStateDto State) : PickResult;

    /// <summary>The category is used, the game is complete, or a simultaneous pick won. State is the truth to resync to.</summary>
    public sealed record Conflict(GameStateDto State) : PickResult;

    /// <summary>No such game for this player. Never distinguishes "not yours" from "does not exist".</summary>
    public sealed record NotFound : PickResult;

    public sealed record UnknownCategory : PickResult;
}

public sealed class GameService(
    GameDbContext db,
    IRankingsStore rankings,
    IOptions<ScoringOptions> scoring,
    Random random,
    TimeProvider time,
    ILogger<GameService> logger)
{
    public async Task<GameStateDto> StartPracticeAsync(Guid playerId, CancellationToken ct)
    {
        var snapshot = rankings.Current ?? throw new RankingsUnavailableException();
        var generated = BoardGenerator.Generate(snapshot, scoring.Value, random);
        var now = time.GetUtcNow();

        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Mode = GameMode.Practice,
            StartedAt = now,
            Board = new Board
            {
                RankMode = scoring.Value.RankMode,
                Cap = scoring.Value.Cap,
                RankingsLoadedAt = snapshot.LoadedAt,
                OptimalScore = generated.OptimalScore,
                Content = generated.Content,
                CreatedAt = now,
            },
        };

        db.Games.Add(game);
        await db.SaveChangesAsync(ct);

        return GameStateMapper.ToDto(game, now);
    }

    public async Task<GameStateDto?> GetAsync(Guid playerId, Guid gameId, CancellationToken ct)
    {
        var game = await LoadAsync(playerId, gameId, tracking: false, ct);

        return game is null ? null : GameStateMapper.ToDto(game, time.GetUtcNow());
    }

    public async Task<PickResult> PickAsync(Guid playerId, Guid gameId, string categoryId, CancellationToken ct)
    {
        var game = await LoadAsync(playerId, gameId, tracking: true, ct);
        if (game is null) return new PickResult.NotFound();

        var content = game.Board.Content;
        var categoryIndex = GameStateMapper.IndexOfCategory(content, categoryId);
        if (categoryIndex < 0) return new PickResult.UnknownCategory();

        var now = time.GetUtcNow();
        if (game.CompletedAt is not null || game.Picks.Any(p => p.CategoryId == categoryId))
        {
            return new PickResult.Conflict(GameStateMapper.ToDto(game, now));
        }

        // The server, not the request, decides which country this pick is for.
        var turn = game.TurnIndex;
        game.Picks.Add(new Pick
        {
            TurnIndex = turn,
            CategoryId = categoryId,
            ISO3 = content.Countries[turn].Iso3,
            Score = content.Cells[turn][categoryIndex].Score,
            WasLate = false,
            PickedAt = now,
        });

        game.TurnIndex = turn + 1;
        if (game.TurnIndex == content.Countries.Count)
        {
            game.CompletedAt = now;
            game.TotalScore = game.Picks.Sum(p => p.Score);
        }

        try
        {
            // One SaveChanges is one transaction: the pick insert and the row-versioned game update succeed or fail together.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A simultaneous pick won: the row version moved, or a unique index (turn, category) fired.
            // Also the only place a deadlock or command timeout under load would otherwise vanish silently.
            logger.LogWarning(ex, "Pick on game {GameId} could not be saved; resyncing.", gameId);
            db.ChangeTracker.Clear();
            var current = await LoadAsync(playerId, gameId, tracking: false, ct);

            return current is null ? new PickResult.NotFound() : new PickResult.Conflict(GameStateMapper.ToDto(current, now));
        }

        return new PickResult.Ok(GameStateMapper.ToDto(game, now));
    }

    private Task<Game?> LoadAsync(Guid playerId, Guid gameId, bool tracking, CancellationToken ct)
    {
        var games = db.Games.Include(g => g.Board).Include(g => g.Picks).AsQueryable();
        if (!tracking) games = games.AsNoTracking();

        return games.SingleOrDefaultAsync(g => g.Id == gameId && g.PlayerId == playerId, ct);
    }
}
