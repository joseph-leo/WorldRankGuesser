namespace WorldRankGuesser.Api.Games;

public sealed record StartGameRequest(string? Mode);

public sealed record PickRequest(string? CategoryId);

public sealed record CategoryDto(string Id, string Name);

public sealed record CountryDto(string Iso3, string Iso2, string Name);

public sealed record CellDto(
    int Score,
    int? CountryRank,
    int? EntryRank,
    bool Unranked,
    string? Sport,
    string? Event,
    string? Gender,
    string? Competitor);

/// <summary>Score is what the pick cost (the cap when late); Result is what the board says for that country and category.</summary>
public sealed record PickDto(int TurnIndex, string CategoryId, CountryDto Country, int Score, bool WasLate, CellDto Result);

public sealed record GridDto(IReadOnlyList<CountryDto> Countries, IReadOnlyList<IReadOnlyList<CellDto>> Cells);

/// <summary>
/// CurrentCountry is the only country not yet picked that a response ever contains.
/// TotalScore, OptimalScore and Grid are null until IsComplete.
/// </summary>
public sealed record GameStateDto(
    Guid Id,
    string Mode,
    DateOnly? DailyDate,
    string RankMode,
    int Cap,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<PickDto> Picks,
    CountryDto? CurrentCountry,
    DateTimeOffset? Deadline,
    DateTimeOffset ServerNow,
    bool IsComplete,
    int? TotalScore,
    int? OptimalScore,
    GridDto? Grid);
