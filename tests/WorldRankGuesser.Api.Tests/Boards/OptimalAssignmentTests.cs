using WorldRankGuesser.Api.Boards;

namespace WorldRankGuesser.Api.Tests.Boards;

public class OptimalAssignmentTests
{
    [Fact]
    public void Picks_the_cheaper_diagonal()
    {
        int[][] cost = [[1, 100], [100, 1]];

        Assert.Equal(2, OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Greedy_is_not_optimal()
    {
        // Greedy takes 1 for the first country and is then forced into 100.
        int[][] cost = [[1, 2], [2, 100]];

        Assert.Equal(4, OptimalAssignment.MinTotal(cost));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Matches_brute_force_on_random_matrices(int seed)
    {
        var random = new Random(seed);
        var cost = Enumerable.Range(0, 5)
            .Select(_ => Enumerable.Range(0, 5).Select(_ => random.Next(1, 151)).ToArray())
            .ToArray();

        Assert.Equal(BruteForce(cost, 0, new bool[5]), OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Handles_a_ten_by_ten_board()
    {
        var cost = Enumerable.Range(0, 10)
            .Select(i => Enumerable.Range(0, 10).Select(j => i == j ? 1 : 150).ToArray())
            .ToArray();

        Assert.Equal(10, OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void Rejects_a_matrix_that_is_not_square()
    {
        int[][] cost = [[1, 2, 3], [4, 5, 6]];

        Assert.Throws<ArgumentException>(() => OptimalAssignment.MinTotal(cost));
    }

    [Fact]
    public void A_country_beaten_in_its_best_category_takes_its_next_best()
    {
        // Denmark's best is badminton (3), but Korea is 1 there and poor at soccer, so Denmark takes soccer.
        int[][] cost =
        [
            [3, 20],    // Denmark: badminton, soccer
            [1, 90],    // Korea
        ];

        var solution = OptimalAssignment.Solve(cost);

        Assert.Equal([1, 0], solution.CategoryOfCountry);
        Assert.Equal(21, solution.Total);
    }

    [Fact]
    public void Ties_go_to_the_earliest_category_for_the_earliest_country()
    {
        int[][] cost = [[1, 1, 9], [1, 1, 9], [9, 9, 1]];

        Assert.Equal([0, 1, 2], OptimalAssignment.Solve(cost).CategoryOfCountry);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_assignment_uses_every_category_once_and_adds_up_to_the_brute_force_total(int seed)
    {
        var random = new Random(seed);
        var cost = Enumerable.Range(0, 5)
            .Select(_ => Enumerable.Range(0, 5).Select(_ => random.Next(1, 151)).ToArray())
            .ToArray();

        var solution = OptimalAssignment.Solve(cost);

        Assert.Equal([0, 1, 2, 3, 4], solution.CategoryOfCountry.Order());
        Assert.Equal(BruteForce(cost, 0, new bool[5]), solution.CategoryOfCountry.Select((category, country) => cost[country][category]).Sum());
    }

    private static int BruteForce(int[][] cost, int country, bool[] used)
    {
        if (country == cost.Length) return 0;

        var best = int.MaxValue;
        for (var category = 0; category < cost.Length; category++)
        {
            if (used[category]) continue;
            used[category] = true;
            best = Math.Min(best, cost[country][category] + BruteForce(cost, country + 1, used));
            used[category] = false;
        }

        return best;
    }
}
