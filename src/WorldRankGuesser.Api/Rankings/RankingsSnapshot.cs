using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Rankings;

/// <summary>Immutable in-memory read model of the current rankings, after aliases.</summary>
public sealed class RankingsSnapshot(
    DateTimeOffset loadedAt,
    IReadOnlyList<CategoryDefinition> categories,
    IReadOnlyList<BoardCountry> drawableCountries,
    IReadOnlyDictionary<(string CategoryId, string Iso3), CategoryRank> ranks)
{
    public DateTimeOffset LoadedAt { get; } = loadedAt;

    public IReadOnlyList<CategoryDefinition> Categories { get; } = categories;

    /// <summary>Ordered by ISO3 so a seeded draw is repeatable.</summary>
    public IReadOnlyList<BoardCountry> DrawableCountries { get; } = drawableCountries;

    /// <summary>Null when the country is unranked in the category.</summary>
    public CategoryRank? Find(string categoryId, string iso3) => ranks.GetValueOrDefault((categoryId, iso3));
}
