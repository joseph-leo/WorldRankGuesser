using SportsRankingService.Models;

namespace SportsRankingService.Configuration;

/// <summary>
/// Which feeds a run fetches, from the command line: each <c>--only</c> value is a pattern whose words must appear,
/// in order, among the words of a feed's <see cref="RankingItem.Describe"/> name, so "Cricket" selects every cricket
/// feed, "Cricket ODI" both ODI feeds, "Cricket Women" every women's cricket feed and "Cricket ODI Women" one.
/// No patterns selects every feed.
/// </summary>
public sealed record FeedFilter(IReadOnlyList<string> Patterns)
{
    private const string OnlyOption = "--only";

    public const string Usage = $"usage: SportsRankingService [{OnlyOption} \"<Sport [Event [Gender]]>\"]...";

    public static readonly FeedFilter All = new([]);

    /// <exception cref="ArgumentException">An argument is not <c>--only &lt;pattern&gt;</c>.</exception>
    public static FeedFilter Parse(string[] args)
    {
        List<string> patterns = [];

        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], OnlyOption, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown argument '{args[i]}'. {Usage}");
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException($"{OnlyOption} needs a feed name. {Usage}");
            }

            patterns.Add(args[++i].Trim());
        }

        return new FeedFilter(patterns);
    }

    public bool Matches(RankingItem item) => Patterns.Count == 0 || Patterns.Any(pattern => Matches(pattern, item));

    /// <summary>True when every word of the pattern appears in the feed's name, in order, as a whole word.</summary>
    public static bool Matches(string pattern, RankingItem item)
    {
        string[] wanted = Words(pattern);
        int matched = 0;

        foreach (string word in Words(item.Describe()))
        {
            if (matched < wanted.Length && string.Equals(word, wanted[matched], StringComparison.OrdinalIgnoreCase))
            {
                matched++;
            }
        }

        return matched == wanted.Length;
    }

    private static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
