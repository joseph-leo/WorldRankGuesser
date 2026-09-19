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
