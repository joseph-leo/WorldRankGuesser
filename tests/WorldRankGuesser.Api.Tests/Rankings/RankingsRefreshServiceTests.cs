using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

/// <summary>
/// The refresh service on a fake clock: nothing here waits in real time except <see cref="Eventually"/>, which only
/// lets the service's thread-pool continuations run.
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

    private static (RankingsRefreshService Service, RankingsStore Store) Create(ScriptedReader reader, FakeTimeProvider time, int refreshMinutes = 60)
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

    /// <summary>Waits, briefly and in real time, for the service's continuations to reach a state.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The service did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Startup_does_not_wait_for_the_load_and_retries_on_a_backoff_until_it_succeeds()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Down, Down, Rows);
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);       // returns at once; the first attempt runs in the background
        await Eventually(() => reader.Attempts == 1);
        Assert.Null(store.Current);

        time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // the second attempt waits the full 5 seconds

        time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => reader.Attempts == 2);
        Assert.Null(store.Current);

        time.Advance(TimeSpan.FromSeconds(10));                  // 5, then 10: the third attempt lands 15 seconds in
        await Eventually(() => store.Current is not null);
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

        await service.StartAsync(CancellationToken.None);
        await Eventually(() => store.Current is not null);
        var first = store.Current;

        time.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(50);
        Assert.Equal(1, reader.Attempts);                        // no backoff once a snapshot exists: the next try is on the hour

        time.Advance(TimeSpan.FromMinutes(1));
        await Eventually(() => reader.Attempts == 2);
        await Task.Delay(50);
        Assert.Same(first, store.Current);                       // the failed refresh kept the previous snapshot

        time.Advance(TimeSpan.FromMinutes(60));
        await Eventually(() => reader.Attempts == 3);
        await Eventually(() => !ReferenceEquals(first, store.Current));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_view_with_too_few_countries_to_draw_a_board_does_not_count_as_loaded()
    {
        var time = new FakeTimeProvider(TestData.LoadedAt);
        var reader = new ScriptedReader(Array.Empty<CountryRankingRow>(), Rows);   // the scraper has not run yet, then it has
        var (service, store) = Create(reader, time);

        await service.StartAsync(CancellationToken.None);
        await Eventually(() => reader.Attempts == 1);
        await Task.Delay(50);
        Assert.Null(store.Current);                              // /readyz stays 503 rather than offering an undrawable board

        time.Advance(TimeSpan.FromSeconds(5));
        await Eventually(() => store.Current is not null);
        Assert.Equal(2, reader.Attempts);

        await service.StopAsync(CancellationToken.None);
    }
}
