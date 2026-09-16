namespace SportsRankingService.Parsing;

/// <summary>
/// Turns one feed response into a ranking. A parser is a pure function of the response text:
/// it knows nothing about which sport, event or gender it is being used for.
/// Implementations are registered as <see cref="IRankingParser"/> singletons and indexed by
/// <see cref="SourceName"/>, which is the value a <c>RankingItem.Source</c> in serviceconfig.json refers to.
/// </summary>
public interface IRankingParser
{
    string SourceName { get; }

    /// <exception cref="ParseException">The response does not have the expected shape.</exception>
    ParsedRanking Parse(string response);
}
