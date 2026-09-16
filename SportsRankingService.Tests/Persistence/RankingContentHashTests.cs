using SportsRankingService.Parsing;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Persistence;

/// <summary>
/// The hash is a release's identity: same rows and same federation date means the same release.
/// A scrape date is not part of it, so re-scraping an unchanged dateless feed changes nothing.
/// </summary>
public class RankingContentHashTests
{
    private static RankingSnapshot Snapshot(DateOnly date, bool federation, params RankEntry[] entries) =>
        new("Field Hockey", "Outdoor", "Men", date, federation, entries);

    [Fact]
    public void Is_32_bytes_and_deterministic()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands"));
        RankingSnapshot b = Snapshot(new(2026, 9, 12), true, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands"));

        Assert.Equal(32, RankingContentHash.Compute(a).Length);
        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Changes_when_a_row_changes()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new(1, "DEU"), new(2, "NLD"));
        RankingSnapshot b = Snapshot(new(2026, 9, 12), true, new(1, "NLD"), new(2, "DEU"));

        Assert.NotEqual(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Changes_when_the_federation_date_changes()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new RankEntry(1, "DEU"));
        RankingSnapshot b = Snapshot(new(2026, 9, 19), true, new RankEntry(1, "DEU"));

        Assert.NotEqual(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Ignores_a_scrape_date()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), false, new RankEntry(1, "DEU"));
        RankingSnapshot b = Snapshot(new(2026, 9, 19), false, new RankEntry(1, "DEU"));

        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Ignores_the_feed_identity_because_the_repository_scopes_by_feed()
    {
        RankingSnapshot a = new("Rugby", "Union", "Men", new(2026, 9, 12), false, [new(1, "ZAF")]);
        RankingSnapshot b = new("Rugby", "Union", "Women", new(2026, 9, 12), false, [new(1, "ZAF")]);

        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }
}
