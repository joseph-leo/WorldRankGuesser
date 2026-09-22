namespace WorldRankGuesser.Api.Boards;

public sealed record BoardCategory(string Id, string Name);

public sealed record BoardCountry(string Iso3, string Iso2, string Name);

/// <summary>
/// One country in one category. Ranks and feed details are null when Unranked. RankedAs is the team an inherited
/// rank came from, null when the rank is the country's own; boards stored before it existed read it as null.
/// </summary>
public sealed record BoardCell(
    int Score,
    int? CountryRank,
    int? EntryRank,
    bool Unranked,
    string? Sport,
    string? Event,
    string? Gender,
    string? Competitor,
    string? RankedAs = null);

/// <summary>Immutable. Cells[countryIndex][categoryIndex]; countries are in draw order.</summary>
public sealed record BoardContent(
    IReadOnlyList<BoardCategory> Categories,
    IReadOnlyList<BoardCountry> Countries,
    IReadOnlyList<IReadOnlyList<BoardCell>> Cells);
