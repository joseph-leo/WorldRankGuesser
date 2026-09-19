using WorldRankGuesser.Api.Rankings;
using static WorldRankGuesser.Api.Tests.Rankings.TestData;

namespace WorldRankGuesser.Api.Tests.Rankings;

public class RankingsSnapshotBuilderTests
{
    private static RankingsSnapshot Build(IReadOnlyList<CountryRankingRow> rows, int minCategoriesRanked = 1) =>
        RankingsSnapshotBuilder.Build(rows, Options(minCategoriesRanked), Catalog, LoadedAt);

    [Fact]
    public void Country_rank_is_competition_ranking_of_countries_by_best_entry()
    {
        // The view has one row per country per feed: that country's best entry.
        var snapshot = Build(
        [
            Row("Badminton", "Singles", "Men", 1, "CHN"),
            Row("Badminton", "Singles", "Men", 3, "DNK"),
            Row("Badminton", "Singles", "Men", 3, "IDN"),
            Row("Badminton", "Singles", "Men", 9, "JPN"),
        ]);

        Assert.Equal(1, snapshot.Find("badminton", "CHN")!.BestByCountry.CountryRank);
        Assert.Equal(2, snapshot.Find("badminton", "DNK")!.BestByCountry.CountryRank);
        Assert.Equal(2, snapshot.Find("badminton", "IDN")!.BestByCountry.CountryRank);
        Assert.Equal(4, snapshot.Find("badminton", "JPN")!.BestByCountry.CountryRank);
        Assert.Equal(9, snapshot.Find("badminton", "JPN")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void Best_feed_is_chosen_separately_for_each_mode()
    {
        var snapshot = Build(
        [
            Row("Badminton", "Singles", "Men", 1, "CHN"),
            Row("Badminton", "Singles", "Men", 3, "DNK", "Viktor Axelsen"),
            Row("Badminton", "Doubles", "Men", 5, "DNK", "Kim Astrup"),
            Row("Badminton", "Doubles", "Men", 8, "CHN"),
        ]);

        var denmark = snapshot.Find("badminton", "DNK")!;

        Assert.Equal("Singles", denmark.BestByEntry.Event);
        Assert.Equal(3, denmark.BestByEntry.EntryRank);
        Assert.Equal("Doubles", denmark.BestByCountry.Event);
        Assert.Equal(1, denmark.BestByCountry.CountryRank);
        Assert.Equal("Kim Astrup", denmark.BestByCountry.Competitor);
    }

    [Fact]
    public void A_category_spans_every_sport_it_lists()
    {
        var snapshot = Build(
        [
            Row("Field Hockey", "Outdoor", "Men", 4, "IND"),
            Row("Ice Hockey", null, "Men", 2, "JPN"),
        ]);

        Assert.Equal("Field Hockey", snapshot.Find("hockey", "IND")!.BestByEntry.Sport);
        Assert.Equal("Ice Hockey", snapshot.Find("hockey", "JPN")!.BestByEntry.Sport);
    }

    [Fact]
    public void Sports_outside_every_category_are_ignored()
    {
        var snapshot = Build([Row("Curling", null, "Men", 1, "JPN")]);

        Assert.Empty(snapshot.DrawableCountries);
    }

    [Fact]
    public void An_unranked_pair_is_null()
    {
        var snapshot = Build([Row("Soccer", null, "Men", 1, "JPN")]);

        Assert.Null(snapshot.Find("cricket", "JPN"));
        Assert.Null(snapshot.Find("soccer", "AUS"));
    }

    [Fact]
    public void The_united_kingdom_inherits_the_best_home_nation_in_every_category()
    {
        var snapshot = Build(
        [
            Row("Soccer", null, "Men", 4, "ENG"),
            Row("Soccer", null, "Men", 40, "SCO"),
            Row("Badminton", "Singles", "Men", 20, "GBR"),
        ]);

        Assert.Equal(4, snapshot.Find("soccer", "GBR")!.BestByEntry.EntryRank);
        Assert.Equal(20, snapshot.Find("badminton", "GBR")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void West_indies_members_inherit_only_the_cricket_rank()
    {
        var snapshot = Build(
        [
            Row("Cricket", "ODI", "Men", 9, "WI"),
            Row("Soccer", null, "Men", 2, "WI"),      // not real data; proves the category filter
            Row("Soccer", null, "Men", 60, "JAM"),
        ]);

        Assert.Equal(9, snapshot.Find("cricket", "JAM")!.BestByEntry.EntryRank);
        Assert.Equal(60, snapshot.Find("soccer", "JAM")!.BestByEntry.EntryRank);
    }

    [Fact]
    public void Not_drawable_codes_are_left_out_of_the_pool()
    {
        var snapshot = Build(
        [
            Row("Soccer", null, "Men", 4, "ENG"),
            Row("Badminton", "Singles", "Men", 20, "GBR"),
            Row("Cricket", "ODI", "Men", 9, "WI"),
        ]);

        Assert.Equal(["GBR"], snapshot.DrawableCountries.Select(c => c.Iso3));
        Assert.Equal("GB", snapshot.DrawableCountries[0].Iso2);
        Assert.Equal("United Kingdom", snapshot.DrawableCountries[0].Name);
    }

    [Fact]
    public void The_pool_is_ordered_by_iso3_and_filtered_by_min_categories_ranked()
    {
        IReadOnlyList<CountryRankingRow> rows =
        [
            Row("Soccer", null, "Men", 1, "JPN"),
            Row("Cricket", "ODI", "Men", 1, "IND"),
            Row("Soccer", null, "Men", 2, "IND"),
            Row("Soccer", null, "Men", 3, "AUS"),
        ];

        Assert.Equal(["AUS", "IND", "JPN"], Build(rows).DrawableCountries.Select(c => c.Iso3));
        Assert.Equal(["IND"], Build(rows, minCategoriesRanked: 2).DrawableCountries.Select(c => c.Iso3));
    }

    [Fact]
    public void A_drawable_country_without_an_iso2_code_fails_loudly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build([Row("Soccer", null, "Men", 1, "ZZZ")]));

        Assert.Contains("ZZZ", error.Message);
    }

    [Fact]
    public void The_snapshot_carries_its_load_time_and_categories()
    {
        var snapshot = Build([]);

        Assert.Equal(LoadedAt, snapshot.LoadedAt);
        Assert.Equal(["soccer", "cricket", "badminton", "hockey"], snapshot.Categories.Select(c => c.Id));
    }
}
