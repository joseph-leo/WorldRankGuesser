using System.Numerics;

namespace WorldRankGuesser.Api.Boards;

/// <summary>CategoryOfCountry[i] is the category index the i-th country takes.</summary>
public sealed record OptimalSolution(int Total, IReadOnlyList<int> CategoryOfCountry);

/// <summary>The lowest total of assigning each country to a different category. Bitmask DP, O(n * 2^n).</summary>
public static class OptimalAssignment
{
    private const int MaxSize = 20;

    public static int MinTotal(IReadOnlyList<IReadOnlyList<int>> cost) => Solve(cost).Total;

    /// <summary>
    /// Among equally good assignments the result is the first in country order: the earliest country takes the
    /// earliest category it can, so the same board always gives the same chart.
    /// </summary>
    public static OptimalSolution Solve(IReadOnlyList<IReadOnlyList<int>> cost)
    {
        var n = cost.Count;
        if (n > MaxSize || cost.Any(row => row.Count != n))
        {
            throw new ArgumentException($"The cost matrix must be square and at most {MaxSize}x{MaxSize}.", nameof(cost));
        }

        // best[mask] = lowest total for the countries from popcount(mask) onwards, given the categories in mask are taken.
        var full = (1 << n) - 1;
        var best = new int[1 << n];

        for (var mask = full - 1; mask >= 0; mask--)
        {
            var country = BitOperations.PopCount((uint)mask);
            best[mask] = int.MaxValue;

            for (var category = 0; category < n; category++)
            {
                if ((mask & (1 << category)) != 0) continue;

                best[mask] = Math.Min(best[mask], cost[country][category] + best[mask | (1 << category)]);
            }
        }

        var categoryOfCountry = new int[n];
        var taken = 0;
        for (var country = 0; country < n; country++)
        {
            for (var category = 0; category < n; category++)
            {
                if ((taken & (1 << category)) != 0) continue;
                if (cost[country][category] + best[taken | (1 << category)] != best[taken]) continue;

                categoryOfCountry[country] = category;
                taken |= 1 << category;
                break;
            }
        }

        return new OptimalSolution(best[0], categoryOfCountry);
    }
}
