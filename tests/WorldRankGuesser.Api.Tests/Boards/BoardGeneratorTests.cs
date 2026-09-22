using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;
using static WorldRankGuesser.Api.Tests.Rankings.TestData;

namespace WorldRankGuesser.Api.Tests.Boards;

public class BoardGeneratorTests
{
    private static readonly ScoringOptions Scoring = new() { RankMode = RankMode.Country, Cap = 150 };

    private const int MaxCapPicks = 1;

    // Four categories (TestData.Options), six ranked countries.
    private static readonly RankingsSnapshot Snapshot = RankingsSnapshotBuilder.Build(
    [
        Row("Soccer", null, "Men", 1, "JPN"),
        Row("Soccer", null, "Men", 2, "AUS"),
        Row("Soccer", null, "Men", 3, "IND"),
        Row("Cricket", "ODI", "Men", 1, "IND"),
        Row("Cricket", "ODI", "Men", 2, "AUS"),
        Row("Badminton", "Singles", "Men", 1, "CHN"),
        Row("Badminton", "Singles", "Men", 2, "DNK"),
        Row("Badminton", "Singles", "Men", 3, "IDN"),
        Row("Field Hockey", "Outdoor", "Men", 1, "IND"),
    ], Options(), Scoring.Cap, Catalog, LoadedAt);

    [Fact]
    public void Draws_one_distinct_country_per_category()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(1)).Content;

        Assert.Equal(["soccer", "cricket", "badminton", "hockey"], board.Categories.Select(c => c.Id));
        Assert.Equal(4, board.Countries.Count);
        Assert.Equal(4, board.Countries.Select(c => c.Iso3).Distinct().Count());
        Assert.Equal(4, board.Cells.Count);
        Assert.All(board.Cells, row => Assert.Equal(4, row.Count));
    }

    [Fact]
    public void The_same_seed_draws_the_same_board()
    {
        var first = BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(7)).Content;
        var second = BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(7)).Content;

        Assert.Equal(first.Countries, second.Countries);
    }

    [Fact]
    public void Different_seeds_draw_different_boards()
    {
        var draws = Enumerable.Range(0, 20)
            .Select(seed => string.Join(",", BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(seed)).Content.Countries.Select(c => c.Iso3)))
            .Distinct()
            .Count();

        Assert.True(draws > 1);
    }

    [Fact]
    public void Every_cell_is_the_scoring_engines_answer()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(3)).Content;

        for (var country = 0; country < board.Countries.Count; country++)
        for (var category = 0; category < board.Categories.Count; category++)
        {
            var expected = ScoringEngine.Score(
                Snapshot.Find(board.Categories[category].Id, board.Countries[country].Iso3), Scoring.RankMode, Scoring.Cap);

            Assert.Equal(expected, board.Cells[country][category]);
        }
    }

    [Fact]
    public void The_optimal_score_is_the_minimum_assignment_of_the_grid()
    {
        var generated = BoardGenerator.Generate(Snapshot, Scoring, MaxCapPicks, new Random(3));
        var cost = generated.Content.Cells.Select(row => row.Select(cell => cell.Score).ToArray()).ToArray();

        Assert.Equal(OptimalAssignment.MinTotal(cost), generated.OptimalScore);
    }

    [Fact]
    public void A_pool_smaller_than_the_category_count_is_an_error()
    {
        var tiny = RankingsSnapshotBuilder.Build([Row("Soccer", null, "Men", 1, "JPN")], Options(), Scoring.Cap, Catalog, LoadedAt);

        var error = Assert.Throws<InvalidOperationException>(() => BoardGenerator.Generate(tiny, Scoring, MaxCapPicks, new Random(1)));

        Assert.Contains("1", error.Message);
        Assert.Contains("4", error.Message);
    }

    // Four all-rounders ranked in every category and four countries ranked in soccer only. Only one of a board's
    // soccer-only countries can take soccer, so a board that draws k of them has k - 1 cap picks in its optimal.
    private static readonly string[] AllRounders = ["AUS", "CHN", "IND", "JPN"];
    private static readonly string[] SoccerOnly = ["DNK", "GBR", "IDN", "JAM"];

    private static readonly RankingsSnapshot Lopsided = RankingsSnapshotBuilder.Build(
    [
        .. AllRounders.SelectMany((iso3, i) => new[]
        {
            Row("Soccer", null, "Men", (short)(i + 1), iso3),
            Row("Cricket", "ODI", "Men", (short)(i + 1), iso3),
            Row("Badminton", "Singles", "Men", (short)(i + 1), iso3),
            Row("Field Hockey", "Outdoor", "Men", (short)(i + 1), iso3),
        }),
        .. SoccerOnly.Select((iso3, i) => Row("Soccer", null, "Men", (short)(i + 5), iso3)),
    ], Options(), Scoring.Cap, Catalog, LoadedAt);

    private static int SoccerOnlyDrawn(GeneratedBoard generated) =>
        generated.Content.Countries.Count(c => SoccerOnly.Contains(c.Iso3));

    [Fact]
    public void A_board_whose_optimal_has_too_many_cap_picks_is_drawn_again()
    {
        var boards = Enumerable.Range(0, 200).Select(seed => BoardGenerator.Generate(Lopsided, Scoring, MaxCapPicks, new Random(seed))).ToList();

        Assert.All(boards, board => Assert.InRange(SoccerOnlyDrawn(board), 0, 2));
        Assert.All(boards, board => Assert.InRange(board.CapPicksInOptimal, 0, MaxCapPicks));
    }

    [Fact]
    public void Cap_picks_in_optimal_counts_the_cap_scored_cells_of_the_optimal_assignment()
    {
        var unlimited = Enumerable.Range(0, 200).Select(seed => BoardGenerator.Generate(Lopsided, Scoring, maxCapPicksInOptimal: 4, new Random(seed))).ToList();

        Assert.All(unlimited, board => Assert.Equal(Math.Max(0, SoccerOnlyDrawn(board) - 1), board.CapPicksInOptimal));
        Assert.Contains(unlimited, board => board.CapPicksInOptimal > MaxCapPicks);   // so the limit above is doing something
    }

    [Fact]
    public void A_first_draw_within_the_limit_is_kept()
    {
        foreach (var seed in Enumerable.Range(0, 200))
        {
            var unlimited = BoardGenerator.Generate(Lopsided, Scoring, maxCapPicksInOptimal: 4, new Random(seed));
            if (unlimited.CapPicksInOptimal > MaxCapPicks) continue;

            var limited = BoardGenerator.Generate(Lopsided, Scoring, MaxCapPicks, new Random(seed));

            Assert.Equal(unlimited.Content.Countries, limited.Content.Countries);
        }
    }

    [Fact]
    public void The_same_seed_draws_the_same_board_when_boards_are_drawn_again()
    {
        foreach (var seed in Enumerable.Range(0, 50))
        {
            var first = BoardGenerator.Generate(Lopsided, Scoring, MaxCapPicks, new Random(seed)).Content;
            var second = BoardGenerator.Generate(Lopsided, Scoring, MaxCapPicks, new Random(seed)).Content;

            Assert.Equal(first.Countries, second.Countries);
        }
    }

    [Fact]
    public void A_pool_that_can_never_meet_the_limit_still_gets_a_board()
    {
        // Soccer-only countries and nothing else: every board has three cap picks in its optimal.
        var hopeless = RankingsSnapshotBuilder.Build(
            [.. SoccerOnly.Select((iso3, i) => Row("Soccer", null, "Men", (short)(i + 1), iso3))],
            Options(), Scoring.Cap, Catalog, LoadedAt);

        var generated = BoardGenerator.Generate(hopeless, Scoring, MaxCapPicks, new Random(1));

        Assert.Equal(4, generated.Content.Countries.Count);
        Assert.Equal(3, generated.CapPicksInOptimal);
    }
}
