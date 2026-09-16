using System.Text.Json;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return JSON. Subclasses declare a small DTO type holding only the
/// properties they need (unknown JSON properties are ignored) and map it to entries.
/// </summary>
public abstract class JsonRankingParser<TRoot> : IRankingParser
{
    // Web defaults: camelCase, case-insensitive property matching, numbers may arrive as strings.
    protected static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public abstract string SourceName { get; }

    protected abstract IEnumerable<RankEntry> Map(TRoot root);

    public IReadOnlyList<RankEntry> Parse(string response)
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

        return Map(root).ToList();
    }
}
