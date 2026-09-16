namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// Feeds with no usable source as of 2026-09-15. Each skip names what would unblock it.
/// The corresponding serviceconfig.json items are disabled with the same note.
/// </summary>
public class MissingSourcesTests
{
    [Fact(Skip = "https://www.iihf.com/en/worldranking returns 403 to scripted clients. Needs a new source for the IIHF world ranking.")]
    public void IceHockey_world_ranking()
    {
    }

    [Fact(Skip = "https://www.atptour.com/en/rankings/doubles sits behind a Cloudflare challenge (403 even with ?rankRange=0-5000). ESPN's API does not carry doubles. Needs a new source.")]
    public void AtpDoubles_ranking()
    {
    }

    [Fact(Skip = "extranet-lv.bwfbadminton.com answers curl with a browser User-Agent but returns 403 to .NET HttpClient (SslStream and WinHTTP alike), i.e. Cloudflare TLS fingerprinting. BwfParser and its fixtures are ready; the feed is disabled in serviceconfig.json until a fetch path exists.")]
    public void Bwf_live_fetch()
    {
    }
}
