namespace SportsRankingService.Parsing;

public sealed class ParseException(string sourceName, string message, Exception? inner = null)
    : Exception($"{sourceName}: {message}", inner)
{
    public string SourceName { get; } = sourceName;
}
