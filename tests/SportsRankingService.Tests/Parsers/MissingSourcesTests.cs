namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// Feeds with no usable source as of 2026-09-18. Each skip names what would unblock it.
/// The corresponding serviceconfig.json items are disabled with the same note.
/// </summary>
public class MissingSourcesTests
{
    [Fact(Skip = "https://www.atptour.com/en/rankings/doubles sits behind a Cloudflare challenge (403 even with ?rankRange=0-5000). ESPN's API does not carry doubles. Needs a new source.")]
    public void AtpDoubles_ranking()
    {
    }
}
