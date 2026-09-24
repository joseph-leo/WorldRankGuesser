using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

/// <summary>
/// The refresh service on a fake clock. The clock only moves when the test calls <see cref="FakeTimeProvider.Advance(TimeSpan)"/>,
/// so no assertion here ever advances it before checking a state the service already set — that is what let the
/// service's timer registrations race a test's advances before this rewrite. A state the first attempt sets at once
/// is settled with <see cref="Holds"/> alone (real time only, never the fake clock). A state that needs a timer to
/// fire is awaited with <see cref="AdvanceUntil"/>, which advances the clock a slice at a time — never the whole
/// distance in one jump — so a timer the service registers just after one slice is still caught by a later one,
/// instead of its due time being skipped over.
/// </summary>
public class RankingsRefreshServiceTests
{
    /// <summary>Answers each read from a queue: an <see cref="Exception"/> is thrown, rows are returned; an empty queue returns <see cref="Rows"/>.</summary>
    private sealed class ScriptedReader(params object[] answers) : IRankingsReader
    {
        private readonly Queue<object> _answers = new(answers);

        public int Attempts { get; private set; }

        public Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct)
        {
            Attempts++;
            var answer = _answers.Count > 0 ? _answers.Dequeue() : Rows;

            return answer is Exception error
                ? Task.FromException<IReadOnlyList<CountryRankingRow>>(error)
                : Task.FromResult((IReadOnlyList<CountryRankingRow>)answer);
        }
    }

    /// <summary>One country per category of TestData.Options(), so the snapshot can fill a board.</summary>
    private static readonly CountryRankingRow[] Rows =
    [
        TestData.Row("Soccer", null, "Men", 1, "JPN"),
        TestData.Row("Cricket", "ODI", "Men", 1, "IND"),
        TestData.Row("Badminton", "Singles", "Men", 1, "DNK"),
        TestData.Row("Field Hockey", "Outdoor", "Men", 1, "AUS"),
    ];

    private static readonly Exception Down = new InvalidOperationException("The database is resuming.");

    private static (RankingsRefreshService Service, RankingsStore Store) Create(IRankingsReader reader, FakeTimeProvider time, int refreshMinutes = 60)
    {
        var store = new RankingsStore();
        var scopes = new ServiceCollection()
            .AddScoped<IRankingsReader>(_ => reader)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var service = new RankingsRefreshService(
            scopes,
            store,
            Options.Create(TestData.Options()),
            Options.Create(new ScoringOptions()),
            Options.Create(new RankingsOptions { RefreshMinutes = refreshMinutes }),
            TestData.Catalog,
            time,
            NullLogger<RankingsRefreshService>.Instance);

        return (service, store);
    }

    /// <summary>Settles: polls briefly in real time so the service's already-due continuations get a chance to run, then reports whether the condition holds.</summary>
    private static async Task<bool> Holds(Func<bool> condition)
    {
        for (var i = 0; i < 20; i++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }

    /// <summary>
    /// Advances the fake clock a slice at a time, settling with <see cref="Holds"/> after each one, until the
    /// condition holds. Throws once the clock has moved forward by <paramref name="within"/> without the condition
    /// holding; <paramref name="within"/> is the expected wait plus a few slices of slack (more than one where a
    /// timer's continuation shares the process with other tests' background work and needs more than one settle to
    /// land), so a genuinely wrong delay still fails the test instead of looping forever.
    /// </summary>
    private static async Task AdvanceUntil(FakeTimeProvider time, Func<bool> condition, TimeSpan slice, TimeSpan within)
    {
        var advanced = TimeSpan.Zero;

        while (true)
        {
            if (await Holds(condition)) return;
            if (advanced >= within) throw new TimeoutException($"The service did not reach the expected state within {within} of fake time.");

            time.Advance(slice);
            advanced += slice;
        }
    }

    [Fact]
    public async Task Startup_does_not_wait_for_the_load_and_retries_on_a_backoff_until_it_succeeds()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Down, Down, Rows);
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);       // the first attempt runs at once; Holds only lets its continuation land
        Assert.True(await Holds(() => reader.Attempts == 1));
        Assert.Null(store.Current);

        time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // the second attempt waits the full 5 seconds

        await AdvanceUntil(time, () => reader.Attempts == 2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));
        Assert.Null(store.Current);

        await AdvanceUntil(time, () => store.Current is not null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(11));   // 5, then 10: the third attempt lands 15 seconds in
        Assert.Equal(3, reader.Attempts);
        Assert.Equal(4, store.Current!.DrawableCountries.Count);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task After_the_first_load_it_refreshes_every_RefreshMinutes_and_a_failed_refresh_keeps_the_snapshot()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Rows, Down, Rows);
        var (service, store) = Create(reader, time, refreshMinutes: 60);

        await service.StartAsync(CancellationToken.None);       // the first attempt runs at once; Holds only lets its continuation land
        Assert.True(await Holds(() => store.Current is not null));
        var first = store.Current;

        time.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // no backoff once a snapshot exists: the next try is on the hour

        await AdvanceUntil(time, () => reader.Attempts == 2, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(8));
        await Task.Delay(50);
        Assert.Same(first, store.Current);                       // the failed refresh kept the previous snapshot

        await AdvanceUntil(time, () => !ReferenceEquals(first, store.Current), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(61));
        Assert.Equal(3, reader.Attempts);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_view_with_too_few_countries_to_draw_a_board_does_not_count_as_loaded()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Array.Empty<CountryRankingRow>(), Rows);   // the scraper has not run yet, then it has
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);       // the first attempt runs at once; Holds only lets its continuation land
        Assert.True(await Holds(() => reader.Attempts == 1));
        Assert.Null(store.Current);                              // /readyz stays 503 rather than offering an undrawable board

        await AdvanceUntil(time, () => store.Current is not null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6));
        Assert.Equal(2, reader.Attempts);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_view_one_country_short_of_a_board_does_not_count_as_loaded_either()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Rows[..3], Rows);        // one category's country short of a board, then a full one
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);       // the first attempt runs at once; Holds only lets its continuation land
        Assert.True(await Holds(() => reader.Attempts == 1));
        Assert.Null(store.Current);                              // /readyz stays 503 rather than offering an undrawable board

        await AdvanceUntil(time, () => store.Current is not null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6));
        Assert.Equal(2, reader.Attempts);

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>Blocks the calling thread inside ReadAsync until released: what SqlClient does while a paused database resumes.</summary>
    private sealed class BlockingReader(ManualResetEventSlim release) : IRankingsReader
    {
        public Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct)
        {
            release.Wait(ct);
            return Task.FromResult((IReadOnlyList<CountryRankingRow>)Rows);
        }
    }

    /// <summary>
    /// SqlClient creates the physical connection synchronously inside OpenAsync, and a paused Azure SQL database holds that
    /// login for tens of seconds while it resumes. The web server starts only after the hosted services, so a hosted
    /// service whose start waits on that login keeps the app from listening (staging, 2026-09-24). The runtime starts a
    /// BackgroundService's ExecuteAsync off the caller's thread; this pins that starting the service returns at once even
    /// when the reader blocks the calling thread.
    /// </summary>
    [Fact]
    public async Task Starting_returns_at_once_even_when_the_first_read_blocks_the_thread()
    {
        using var release = new ManualResetEventSlim(false);
        var (service, store) = Create(new BlockingReader(release), new FakeTimeProvider(TestData.LoadedAt));

        var starting = service.StartAsync(CancellationToken.None);
        var returned = await Task.WhenAny(starting, Task.Delay(TimeSpan.FromSeconds(5))) == starting;

        release.Set();                                           // whatever happened, let the blocked read finish
        Assert.True(returned, "StartAsync waited for a read that blocks the thread; the web server would not be listening.");
        Assert.True(await Holds(() => store.Current is not null));

        await service.StopAsync(CancellationToken.None);
    }
}
