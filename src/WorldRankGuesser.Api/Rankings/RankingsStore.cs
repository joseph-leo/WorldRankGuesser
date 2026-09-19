namespace WorldRankGuesser.Api.Rankings;

public interface IRankingsStore
{
    /// <summary>Null until the first successful load.</summary>
    RankingsSnapshot? Current { get; }

    void Set(RankingsSnapshot snapshot);
}

public sealed class RankingsStore : IRankingsStore
{
    private volatile RankingsSnapshot? _current;

    public RankingsSnapshot? Current => _current;

    public void Set(RankingsSnapshot snapshot) => _current = snapshot;
}
