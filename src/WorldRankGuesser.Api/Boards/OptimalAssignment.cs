using System.Numerics;

namespace WorldRankGuesser.Api.Boards;

/// <summary>The lowest total of assigning each country to a different category. Bitmask DP, O(n * 2^n).</summary>
public static class OptimalAssignment
{
    private const int MaxSize = 20;

    public static int MinTotal(IReadOnlyList<IReadOnlyList<int>> cost)
    {
        var n = cost.Count;
        if (n > MaxSize || cost.Any(row => row.Count != n))
        {
            throw new ArgumentException($"The cost matrix must be square and at most {MaxSize}x{MaxSize}.", nameof(cost));
        }

        // best[mask] = lowest total for the first popcount(mask) countries using exactly the categories in mask.
        var best = new int[1 << n];
        Array.Fill(best, int.MaxValue);
        best[0] = 0;

        for (var mask = 0; mask < best.Length; mask++)
        {
            if (best[mask] == int.MaxValue) continue;

            var country = BitOperations.PopCount((uint)mask);
            if (country == n) continue;

            for (var category = 0; category < n; category++)
            {
                if ((mask & (1 << category)) != 0) continue;

                var next = mask | (1 << category);
                best[next] = Math.Min(best[next], best[mask] + cost[country][category]);
            }
        }

        return best[^1];
    }
}
