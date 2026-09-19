namespace WorldRankGuesser.Api.Configuration;

public sealed class GameOptions
{
    public const string Section = "Game";

    /// <summary>A country is drawable only if it is ranked in at least this many categories.</summary>
    public int MinCategoriesRanked { get; set; } = 1;

    public List<CategoryDefinition> Categories { get; set; } = [];

    public List<AliasRule> Aliases { get; set; } = [];

    /// <summary>Codes in the data that are never drawn (home nations, West Indies).</summary>
    public List<string> NotDrawable { get; set; } = [];

    public static bool IsValid(GameOptions options) =>
        options.MinCategoriesRanked >= 1
        && options.Categories.Count > 0
        && options.Categories.All(c => c.Id.Length > 0 && c.Name.Length > 0 && c.Sports.Count > 0)
        && options.Categories.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == options.Categories.Count;
}

public sealed class CategoryDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The scraper's Sport values this category covers.</summary>
    public List<string> Sports { get; set; } = [];
}

/// <summary>Every target inherits the best result of the sources, in the listed categories (empty = all).</summary>
public sealed class AliasRule
{
    public List<string> Sources { get; set; } = [];

    public List<string> Targets { get; set; } = [];

    public List<string> Categories { get; set; } = [];
}
