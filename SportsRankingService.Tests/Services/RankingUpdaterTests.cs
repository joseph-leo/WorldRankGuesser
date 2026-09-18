using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// <see cref="RankingUpdater"/> with a fake <see cref="IRankingSourceRunner"/> and a recording,
/// scoped fake <see cref="IRankingRepository"/> resolved from a real <see cref="ServiceProvider"/>
/// (so scoping is exercised for real, not simulated).
/// </summary>
public class RankingUpdaterTests
{
    private sealed class FakeOptionsMonitor(RankingSourcesOptions value) : IOptionsMonitor<RankingSourcesOptions>
    {
        public RankingSourcesOptions CurrentValue { get; } = value;

        public RankingSourcesOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<RankingSourcesOptions, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class FakeRunner : IRankingSourceRunner
    {
        private readonly ConcurrentDictionary<string, Func<RankingSnapshot?>> _behaviors = new();

        public ConcurrentBag<string> Called { get; } = [];

        public void Returns(string sport, RankingSnapshot? snapshot) => _behaviors[sport] = () => snapshot;

        public void Throws(string sport, Exception exception) => _behaviors[sport] = () => throw exception;

        public Task<RankingSnapshot?> RunAsync(RankingItem item, CancellationToken cancellationToken)
        {
            // A real runner's HttpClient call would observe cancellation; the fake mirrors that.
            cancellationToken.ThrowIfCancellationRequested();
            Called.Add(item.Sport);
            return Task.FromResult(_behaviors[item.Sport]());
        }
    }

    private sealed class Recorder
    {
        public ConcurrentBag<(string Sport, IRankingRepository Instance)> Saved { get; } = [];

        public ConcurrentDictionary<string, SaveOutcome> Outcomes { get; } = new();
    }

    private sealed class RecordingRepository(Recorder recorder) : IRankingRepository
    {
        public Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken)
        {
            SaveOutcome outcome = recorder.Outcomes.GetValueOrDefault(snapshot.Sport);
            recorder.Saved.Add((snapshot.Sport, this));
            return Task.FromResult(outcome);
        }
    }

    private static (RankingUpdater Updater, FakeRunner Runner, Recorder Recorder) CreateUpdater(params RankingItem[] items)
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddScoped<IRankingRepository, RecordingRepository>();
        ServiceProvider provider = services.BuildServiceProvider();

        FakeRunner runner = new();
        FakeOptionsMonitor sources = new(new RankingSourcesOptions { Rankings = items.ToList() });

        RankingUpdater updater = new(runner, sources, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RankingUpdater>.Instance);

        return (updater, runner, recorder);
    }

    private static RankingItem Item(string sport, bool enabled = true) => new()
    {
        Sport = sport, Gender = "Men", Url = "http://x", Source = "Fih", Enabled = enabled,
    };

    private static RankingSnapshot Snapshot(string sport, int entries = 1) =>
        new(sport, null, "Men", new DateOnly(2026, 9, 15), IsFederationDate: false,
            Enumerable.Range(1, entries).Select(i => new RankingSnapshotEntry((short)i, "DEU")).ToList());

    [Fact]
    public async Task Disabled_items_are_skipped_and_not_counted()
    {
        var (updater, runner, _) = CreateUpdater(Item("Enabled Sport"), Item("Disabled Sport", enabled: false));
        runner.Returns("Enabled Sport", Snapshot("Enabled Sport"));

        UpdateSummary summary = await updater.UpdateAllAsync(FeedFilter.All, CancellationToken.None);

        Assert.Equal(1, summary.Feeds);
        Assert.DoesNotContain("Disabled Sport", runner.Called);
    }

    [Fact]
    public async Task A_feed_with_no_entries_counts_as_failed_and_never_reaches_the_repository()
    {
        var (updater, runner, recorder) = CreateUpdater(Item("Empty Sport"));
        runner.Returns("Empty Sport", Snapshot("Empty Sport", entries: 0));

        UpdateSummary summary = await updater.UpdateAllAsync(FeedFilter.All, CancellationToken.None);

        Assert.Equal(new UpdateSummary(Feeds: 1, Inserted: 0, Unchanged: 0, Failed: 1), summary);
        Assert.Empty(recorder.Saved);
    }

    [Fact]
    public async Task A_throwing_feed_counts_as_failed_while_the_others_still_save()
    {
        var (updater, runner, recorder) = CreateUpdater(Item("Broken Sport"), Item("Inserted Sport"), Item("Unchanged Sport"));
        runner.Throws("Broken Sport", new InvalidOperationException("boom"));
        runner.Returns("Inserted Sport", Snapshot("Inserted Sport"));
        runner.Returns("Unchanged Sport", Snapshot("Unchanged Sport"));
        recorder.Outcomes["Unchanged Sport"] = SaveOutcome.Unchanged;

        UpdateSummary summary = await updater.UpdateAllAsync(FeedFilter.All, CancellationToken.None);

        Assert.Equal(3, summary.Feeds);
        Assert.Equal(1, summary.Inserted);
        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(1, summary.Failed);
        Assert.DoesNotContain(recorder.Saved, c => c.Sport == "Broken Sport");
    }

    [Fact]
    public async Task The_summary_tallies_add_up_to_the_feed_count()
    {
        var (updater, runner, recorder) = CreateUpdater(Item("Broken Sport"), Item("Inserted Sport"), Item("Unchanged Sport"), Item("Empty Sport"));
        runner.Throws("Broken Sport", new InvalidOperationException("boom"));
        runner.Returns("Inserted Sport", Snapshot("Inserted Sport"));
        runner.Returns("Unchanged Sport", Snapshot("Unchanged Sport"));
        runner.Returns("Empty Sport", Snapshot("Empty Sport", entries: 0));
        recorder.Outcomes["Unchanged Sport"] = SaveOutcome.Unchanged;

        UpdateSummary summary = await updater.UpdateAllAsync(FeedFilter.All, CancellationToken.None);

        Assert.Equal(summary.Feeds, summary.Inserted + summary.Unchanged + summary.Failed);
    }

    [Fact]
    public async Task A_cancelled_token_throws_instead_of_counting_a_failure()
    {
        var (updater, runner, _) = CreateUpdater(Item("Any Sport"));
        runner.Returns("Any Sport", Snapshot("Any Sport"));
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => updater.UpdateAllAsync(FeedFilter.All, cts.Token));
    }

    [Fact]
    public async Task A_filter_runs_only_the_matching_enabled_feeds()
    {
        var (updater, runner, _) = CreateUpdater(Item("Cricket"), Item("Soccer"), Item("Volleyball"));
        runner.Returns("Cricket", Snapshot("Cricket"));
        runner.Returns("Soccer", Snapshot("Soccer"));
        runner.Returns("Volleyball", Snapshot("Volleyball"));

        UpdateSummary summary = await updater.UpdateAllAsync(new FeedFilter(["Cricket", "Volleyball Men"]), CancellationToken.None);

        Assert.Equal(2, summary.Feeds);
        Assert.Equal(["Cricket", "Volleyball"], runner.Called.Order());
    }

    [Fact]
    public async Task A_pattern_that_matches_no_enabled_feed_is_a_configuration_error_and_nothing_runs()
    {
        var (updater, runner, _) = CreateUpdater(Item("Cricket"), Item("Badminton", enabled: false));
        runner.Returns("Cricket", Snapshot("Cricket"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.UpdateAllAsync(new FeedFilter(["Cricket", "Badminton"]), CancellationToken.None));

        Assert.Contains("Badminton", ex.Message);
        Assert.Empty(runner.Called);
    }

    [Fact]
    public async Task Each_save_runs_in_its_own_scope()
    {
        var (updater, runner, recorder) = CreateUpdater(Item("Sport A"), Item("Sport B"), Item("Sport C"));
        runner.Returns("Sport A", Snapshot("Sport A"));
        runner.Returns("Sport B", Snapshot("Sport B"));
        runner.Returns("Sport C", Snapshot("Sport C"));

        await updater.UpdateAllAsync(FeedFilter.All, CancellationToken.None);

        Assert.Equal(3, recorder.Saved.Select(c => c.Instance).Distinct().Count());
    }
}
