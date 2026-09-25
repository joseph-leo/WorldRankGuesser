using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>What one refresh did. <see cref="Error"/> is set only when <see cref="Loaded"/> is false; the previous snapshot then stays.</summary>
public sealed record RefreshResult(bool Loaded, int Rows, int DrawableCountries, DateTimeOffset LoadedAt, string? Error);

/// <summary>
/// The one place the rankings view is read into a snapshot and stored: the background timer
/// (<see cref="RankingsRefreshService"/>) and the refresh endpoint both call it. A failed read keeps the previous
/// snapshot, so a refresh can never blank the game. One refresh runs at a time: a call that arrives while one is in
/// flight awaits that refresh and returns its result, so two callers never wake the paused database twice for nothing.
/// </summary>
public sealed class RankingsRefresher(
    IServiceScopeFactory scopes,
    IRankingsStore store,
    IOptions<GameOptions> gameOptions,
    IOptions<ScoringOptions> scoringOptions,
    CountryCatalog catalog,
    TimeProvider time,
    ILogger<RankingsRefresher> logger)
{
    private readonly object _gate = new();
    private Task<RefreshResult>? _inFlight;

    public Task<RefreshResult> RefreshAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_inFlight is { } inFlight)
            {
                return inFlight;
            }

            // The slot is taken before the read starts, so a read that completes at once (a test reader, a failed
            // scope) still finds its own slot to release; a continuation on the read itself would run first.
            var completion = new TaskCompletionSource<RefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = completion.Task;
            _ = RunAndReleaseAsync(completion, ct);
            return completion.Task;
        }
    }

    /// <summary>Runs one refresh on its first caller's token, frees the slot, then hands the outcome to everyone awaiting it.</summary>
    private async Task RunAndReleaseAsync(TaskCompletionSource<RefreshResult> completion, CancellationToken ct)
    {
        var run = RunAsync(ct);
        await Task.WhenAny(run);    // never throws: the outcome, whatever it was, is copied below

        // Released before the result is handed out, so a caller that refreshes again at once starts a new read.
        lock (_gate)
        {
            _inFlight = null;
        }

        if (run.IsCompletedSuccessfully)
        {
            completion.SetResult(run.Result);
        }
        else if (run.IsCanceled)
        {
            completion.SetCanceled(ct);
        }
        else
        {
            completion.SetException(run.Exception!.InnerExceptions);
        }
    }

    private async Task<RefreshResult> RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IRankingsReader>().ReadAsync(ct);
            var snapshot = RankingsSnapshotBuilder.Build(rows, gameOptions.Value, scoringOptions.Value.Cap, catalog, time.GetUtcNow());

            // A view the scraper has not filled yet cannot draw a board (BoardGenerator needs one country per category);
            // treating it as loaded would make /readyz say ready and every game start fail.
            if (snapshot.DrawableCountries.Count < snapshot.Categories.Count)
            {
                throw new InvalidOperationException(
                    $"The rankings view yields {snapshot.DrawableCountries.Count} drawable countries; a board needs {snapshot.Categories.Count}.");
            }

            store.Set(snapshot);
            logger.LogInformation(
                "Rankings loaded: {Rows} rows, {Countries} drawable countries.", rows.Count, snapshot.DrawableCountries.Count);
            return new RefreshResult(true, rows.Count, snapshot.DrawableCountries.Count, snapshot.LoadedAt, null);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A cold start meeting a resuming database is expected, so those attempts log a warning without
            // claiming a previous snapshot; a failure after a snapshot exists is not expected and stays an error.
            if (store.Current is null)
            {
                logger.LogWarning(error, "Loading the rankings failed; no snapshot yet, trying again.");
            }
            else
            {
                logger.LogError(error, "Loading the rankings failed; keeping the previous snapshot.");
            }

            return new RefreshResult(false, 0, 0, store.Current?.LoadedAt ?? default, error.Message);
        }
    }
}
