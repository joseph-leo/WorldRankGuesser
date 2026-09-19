namespace WorldRankGuesser.Api.Rankings;

public interface IRankingsReader
{
    Task<IReadOnlyList<CountryRankingRow>> ReadAsync(CancellationToken ct);
}
