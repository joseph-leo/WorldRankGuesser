namespace SportsRankingService.Tests.Parsers;

public class BaseballParserTests
{
    [Fact(Skip = "No fixture: rankings.wbsc.org/list/* returns 404 and the new root page loads its rows by a separate Inertia request (captured 2026-09-15). Needs that endpoint.")]
    public void Parses_baseball_and_softball_rankings()
    {
    }
}
