namespace SportsRankingService.Parsing;

/// <summary>
/// Turns one feed response into rank entries. A parser is a pure function of the response text:
/// it knows nothing about which sport, event or gender it is being used for.
/// Implementations are registered as keyed services under <see cref="SourceName"/>, which is the
/// value a <c>RankingItem.Source</c> in serviceconfig.json refers to.
/// </summary>
public interface IRankingParser
{
    string SourceName { get; }

    /// <exception cref="ParseException">The response does not have the expected shape.</exception>
    IReadOnlyList<RankEntry> Parse(string response);
}
