namespace SportsRankingService.Parsing;

/// <summary>
/// Turns one feed response into a ranking. A parser is a pure function of the response text and,
/// for the few feeds whose one response holds several ranking tables, the item's selector; it knows
/// nothing about which sport, event or gender it is being used for.
/// Implementations are registered as <see cref="IRankingParser"/> singletons and indexed by
/// <see cref="SourceName"/>, which is the value a <c>RankingItem.Source</c> in serviceconfig.json refers to.
/// </summary>
public interface IRankingParser
{
    string SourceName { get; }

    /// <param name="response">The fetched response body.</param>
    /// <param name="selector">The item's <c>Selector</c>, which parsers of single-table responses ignore.</param>
    /// <exception cref="ParseException">The response does not have the expected shape, or the selector names nothing in it.</exception>
    ParsedRanking Parse(string response, string? selector = null);
}
