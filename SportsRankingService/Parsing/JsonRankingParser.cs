using System.Text.Json;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return JSON. Subclasses declare a small DTO type holding only the
/// properties they need (unknown JSON properties are ignored), map it to entries, and may
/// override <see cref="GetRankingDate"/> when the feed publishes a ranking date.
/// </summary>
public abstract class JsonRankingParser<TRoot> : IRankingParser
{
    // Web defaults: camelCase, case-insensitive property matching, numbers may arrive as strings.
    protected static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public abstract string SourceName { get; }

    protected abstract IEnumerable<RankEntry> Map(TRoot root);

    /// <summary>The federation's ranking date, or null when the feed has none. Throw <see cref="ParseException"/> for an unreadable value.</summary>
    protected virtual DateOnly? GetRankingDate(TRoot root) => null;

    public ParsedRanking Parse(string response, string? selector = null)
    {
        TRoot root;
        try
        {
            root = JsonSerializer.Deserialize<TRoot>(response, Options)
                   ?? throw new ParseException(SourceName, "response deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new ParseException(SourceName, "response is not the expected JSON shape", ex);
        }

        return new ParsedRanking(Map(root).ToList(), GetRankingDate(root));
    }
}
