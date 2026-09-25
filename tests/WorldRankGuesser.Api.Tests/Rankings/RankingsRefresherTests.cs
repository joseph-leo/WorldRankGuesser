using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Rankings;

/// <summary>
/// The one place a snapshot is read, built and stored: the background timer and the refresh endpoint both call it.
/// A failed read keeps the previous snapshot; two callers at once share one read.
/// </summary>
public class RankingsRefresherTests
{
    /// <summary>Answers each read from a queue: an <see cref="Exception"/> is thrown, rows are returned; an empty queue returns <see cref="Rows"/>. A gate, when set, holds every read open until released.</summary>
    private sealed class ScriptedReader(params object[] answers) : IRankingsReader
    {
        /// <summary>A lone array answer: without this, params array covariance would spread its rows into one answer each.</summary>
        public ScriptedReader(CountryRankingRow[] rows) : this((object)rows)
        {
        }

        private readonly Queue<object> _answers = new(answers);

        public int Attempts { get; private set; }

        public TaskCompletionSource? Gate { get; init; }

        public async Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct)
        {
            Attempts++;
            if (Gate is not null)
            {
                await Gate.Task;
            }

            var answer = _answers.Count > 0 ? _answers.Dequeue() : Rows;
            return answer is Exception error ? throw error : (IReadOnlyList<CountryRankingRow>)answer;
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

    private static (RankingsRefresher Refresher, RankingsStore Store) Create(IRankingsReader reader, FakeTimeProvider? time = null)
    {
        var store = new RankingsStore();
        var scopes = new ServiceCollection()
            .AddScoped<IRankingsReader>(_ => reader)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var refresher = new RankingsRefresher(
            scopes,
            store,
            Options.Create(TestData.Options()),
            Options.Create(new ScoringOptions()),
            TestData.Catalog,
            time ?? new FakeTimeProvider(TestData.LoadedAt),
            NullLogger<RankingsRefresher>.Instance);

        return (refresher, store);
    }

    [Fact]
    public async Task A_load_stores_the_snapshot_and_reports_its_counts()
    {
        var (refresher, store) = Create(new ScriptedReader(Rows));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.True(result.Loaded);
        Assert.Equal(4, result.Rows);
        Assert.Equal(4, result.DrawableCountries);
        Assert.Equal(TestData.LoadedAt, result.LoadedAt);
        Assert.Null(result.Error);
        Assert.Equal(4, store.Current!.DrawableCountries.Count);
    }

    [Fact]
    public async Task A_failed_read_keeps_the_previous_snapshot_and_reports_the_error()
    {
        var (refresher, store) = Create(new ScriptedReader(Rows, Down));

        await refresher.RefreshAsync(CancellationToken.None);
        var first = store.Current;
        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Contains("resuming", result.Error);
        Assert.Same(first, store.Current);
    }

    [Fact]
    public async Task A_first_read_that_fails_leaves_no_snapshot()
    {
        var (refresher, store) = Create(new ScriptedReader(Down));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Null(store.Current);
    }

    /// <summary>A view with fewer drawable countries than categories cannot draw a board, so it does not count as loaded.</summary>
    [Fact]
    public async Task A_view_too_thin_for_a_board_is_refused()
    {
        var (refresher, store) = Create(new ScriptedReader(new[] { Rows[0], Rows[1] }));

        var result = await refresher.RefreshAsync(CancellationToken.None);

        Assert.False(result.Loaded);
        Assert.Contains("drawable countries", result.Error);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Concurrent_calls_share_one_read()
    {
        var reader = new ScriptedReader(Rows) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (refresher, _) = Create(reader);

        var first = refresher.RefreshAsync(CancellationToken.None);
        var second = refresher.RefreshAsync(CancellationToken.None);
        reader.Gate.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, reader.Attempts);
        Assert.All(results, r => Assert.True(r.Loaded));
        Assert.Equal(results[0].LoadedAt, results[1].LoadedAt);
    }

    [Fact]
    public async Task A_call_after_a_finished_refresh_reads_again()
    {
        var reader = new ScriptedReader(Rows, Rows);
        var (refresher, _) = Create(reader);

        await refresher.RefreshAsync(CancellationToken.None);
        await refresher.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, reader.Attempts);
    }
}
