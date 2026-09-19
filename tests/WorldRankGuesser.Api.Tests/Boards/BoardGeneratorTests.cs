using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;
using static WorldRankGuesser.Api.Tests.Rankings.TestData;

namespace WorldRankGuesser.Api.Tests.Boards;

public class BoardGeneratorTests
{
    private static readonly ScoringOptions Scoring = new() { RankMode = RankMode.Country, Cap = 150 };

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
    ], Options(), Catalog, LoadedAt);

    [Fact]
    public void Draws_one_distinct_country_per_category()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, new Random(1)).Content;

        Assert.Equal(["soccer", "cricket", "badminton", "hockey"], board.Categories.Select(c => c.Id));
        Assert.Equal(4, board.Countries.Count);
        Assert.Equal(4, board.Countries.Select(c => c.Iso3).Distinct().Count());
        Assert.Equal(4, board.Cells.Count);
        Assert.All(board.Cells, row => Assert.Equal(4, row.Count));
    }

    [Fact]
    public void The_same_seed_draws_the_same_board()
    {
        var first = BoardGenerator.Generate(Snapshot, Scoring, new Random(7)).Content;
        var second = BoardGenerator.Generate(Snapshot, Scoring, new Random(7)).Content;

        Assert.Equal(first.Countries, second.Countries);
    }

    [Fact]
    public void Different_seeds_draw_different_boards()
    {
        var draws = Enumerable.Range(0, 20)
            .Select(seed => string.Join(",", BoardGenerator.Generate(Snapshot, Scoring, new Random(seed)).Content.Countries.Select(c => c.Iso3)))
            .Distinct()
            .Count();

        Assert.True(draws > 1);
    }

    [Fact]
    public void Every_cell_is_the_scoring_engines_answer()
    {
        var board = BoardGenerator.Generate(Snapshot, Scoring, new Random(3)).Content;

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
        var generated = BoardGenerator.Generate(Snapshot, Scoring, new Random(3));
        var cost = generated.Content.Cells.Select(row => row.Select(cell => cell.Score).ToArray()).ToArray();

        Assert.Equal(OptimalAssignment.MinTotal(cost), generated.OptimalScore);
    }

    [Fact]
    public void A_pool_smaller_than_the_category_count_is_an_error()
    {
        var tiny = RankingsSnapshotBuilder.Build([Row("Soccer", null, "Men", 1, "JPN")], Options(), Catalog, LoadedAt);

        var error = Assert.Throws<InvalidOperationException>(() => BoardGenerator.Generate(tiny, Scoring, new Random(1)));

        Assert.Contains("1", error.Message);
        Assert.Contains("4", error.Message);
    }
}
