# Store Every Parsed Entry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store one `RankingRows` row per parsed entry (athlete, doubles partner, group or team) instead of collapsing to one row per country, and derive country-level facts in a new `CurrentCountryRankings` view.

**Architecture:** The parsers keep emitting one `RankEntry` per ranked entity; `RankingSnapshotBuilder` stops collapsing and instead sorts, fills names, and rejects equal duplicates; the `RankedEntrants` column disappears from storage and reappears as a `COUNT(*)` in a derived view. Two parsers are fixed so they no longer emit duplicates (FIBA sweeps widget tables; BWF stamped the same pair string on both partner rows).

**Tech Stack:** .NET 8 console app, EF Core 8.0.31 (SQL Server live, SQLite in-memory in tests), xUnit fixture-based tests, HtmlAgilityPack, Docker Compose SQL Server 2022.

**Spec:** `docs/superpowers/specs/2026-09-17-store-all-entries-design.md` (read it first; also skim CLAUDE.md — its rules apply to every task).

## Global Constraints

- All commands run from the repo root. Build: `dotnet build SportsRankingService.sln` (warnings are expected; 0 errors). Tests: `dotnet test SportsRankingService.Tests` (no network; expect 3 skipped — that is normal).
- Never make a red fixture test pass by weakening its assertions (CLAUDE.md rule). If a live capture differs from the values written here, update expectations to the *captured file's* real values — the procedure and inspection commands are given in Task 5.
- `RankingSnapshotEntry` equality IS the duplicate guard. It must stay a `sealed record` with only the five stored values as positional parameters; never add mutable members to it.
- The app never migrates itself; migrations are applied with `dotnet ef database update` (repo-local tool: run `dotnet tool restore` once if `dotnet ef` is missing).
- Docker Desktop must be running for Tasks 4 and 6. If `docker compose` fails to parse, check the head of `docker-compose.yml` for stray characters (a known IDE glitch).
- Commit messages: imperative summary line, no prefix convention, ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: BwfParser — each partner's row names its own partner

Today both rows of a doubles pair carry the joined pair string ("KIM Won Ho / SEO Seung Jae") as `Competitor`, so a same-country pair produces two *identical* entries. Each row must instead carry that partner's own display name, which makes partner rows distinct by construction.

**Files:**
- Modify: `SportsRankingService/Parsers/BwfParser.cs`
- Test: `SportsRankingService.Tests/Parsers/BwfParserTests.cs`

**Interfaces:**
- Consumes: `RankEntry(short Position, string ISO3, string? TeamName, string? Competitor, decimal? Points)` — unchanged.
- Produces: BWF doubles entries whose `Competitor` is the single partner's name (e.g. `"KIM Won Ho"`), one entry per partner, pair position and pair points on both. Task 3's tests reuse these exact names.

- [ ] **Step 1: Replace the pair-competitor test with an own-name test**

In `BwfParserTests.cs`, delete the test `Doubles_partners_share_the_pair_as_competitor` and add:

```csharp
[Fact]
public void Doubles_partners_each_carry_their_own_name()
{
    var pair = _parser.Parse(Fixture.Read("Bwf_MensDoubles.json")).Entries.Where(r => r.Position == 1).ToList();

    Assert.Equal(["KIM Won Ho", "SEO Seung Jae"], pair.Select(r => r.Competitor));
    Assert.All(pair, r => Assert.Equal("KOR", r.ISO3));
    Assert.All(pair, r => Assert.Equal(114099m, r.Points));
}
```

Also update the class doc comment's last sentence from "Doubles pairs yield one entry per partner with the same position." to "Doubles pairs yield one entry per partner, each naming its own partner."

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SportsRankingService.Tests --filter BwfParserTests`
Expected: `Doubles_partners_each_carry_their_own_name` FAILS — actual competitor is `"KIM Won Ho / SEO Seung Jae"`. The other five tests pass.

- [ ] **Step 3: Emit the partner's own name**

In `BwfParser.cs`, replace the `Map` method and the `Competitor` helper with:

```csharp
protected override IEnumerable<RankEntry> Map(Root root)
{
    foreach (Row row in root.Results.Data)
    {
        foreach ((Player? player, Country? country) in new[] { (row.Player1, row.Player1Country), (row.Player2, row.Player2Country) })
        {
            if (country?.Name is null || IsNeutral(country.Name))
            {
                continue;
            }

            if (!CountryUtil.TryGetISO3FromCountry(country.Name, out string? iso3))
            {
                throw new ParseException(SourceName, $"no ISO3 mapping for country name '{country.Name}'");
            }

            yield return new RankEntry(row.Rank, iso3!, country.Name, CleanName(player?.NameHtml), row.Points);
        }
    }
}

/// <summary>Strips the name's HTML markup and collapses whitespace; null when the feed gives none.</summary>
private static string? CleanName(string? nameHtml)
{
    if (nameHtml is null)
    {
        return null;
    }

    string name = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(nameHtml, " ")), " ").Trim();
    return name.Length == 0 ? null : name;
}
```

Update the class XML doc sentence "Doubles rows carry two players and yield one entry per partner with the pair's position; both entries name the pair as the competitor." to "…one entry per partner with the pair's position and points; each entry names its own partner."

- [ ] **Step 4: Run the BWF tests to verify they pass**

Run: `dotnet test SportsRankingService.Tests --filter BwfParserTests`
Expected: all 6 PASS. Singles still work because a singles row has only `Player1`, whose own name is what the old join produced. The neutral-athlete count test (190–199) is untouched by the competitor change.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test SportsRankingService.Tests`
Expected: everything green (3 skipped as always).

```powershell
git add SportsRankingService/Parsers/BwfParser.cs SportsRankingService.Tests/Parsers/BwfParserTests.cs
git commit -m @'
Name each BWF doubles partner on their own row

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: FibaParser — read only the main ranking table

The FIBA page holds three tables: the ranking plus "Meilleures Progressions" and "Pires Chutes" widgets whose rows also carry ranks, so `//table//tbody/tr` sweeps 169 rows of which only 159 are distinct countries. The ranking is the document's first table (verified: its 159 team links are exactly the fixture's distinct slugs).

**Files:**
- Modify: `SportsRankingService/Parsers/FibaParser.cs`
- Test: `SportsRankingService.Tests/Parsers/FibaParserTests.cs`

**Interfaces:**
- Produces: 159 distinct-country entries from the men's fixture. No signature changes.

- [ ] **Step 1: Add the failing widget-exclusion test**

In `FibaParserTests.cs` add:

```csharp
[Fact]
public void Ignores_the_movers_and_drops_widget_tables()
{
    // The page also carries "Meilleures Progressions" and "Pires Chutes" tables whose rows repeat ranked countries.
    var rows = _parser.Parse(Fixture.Read("Fiba_Ranking_Men.html")).Entries;

    Assert.Equal(159, rows.Count);
    Assert.Equal(159, rows.Select(r => r.ISO3).Distinct().Count());
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SportsRankingService.Tests --filter FibaParserTests`
Expected: the new test FAILS with count 169.

- [ ] **Step 3: Scope the XPath to the first table**

In `FibaParser.cs` change:

```csharp
protected override string RowXPath => "(//table)[1]//tbody/tr";
```

and append to the class XML doc: "The page also holds 'biggest movers' and 'biggest drops' widget tables after the ranking, so only the first table is read."

- [ ] **Step 4: Run the FIBA tests to verify they pass**

Run: `dotnet test SportsRankingService.Tests --filter FibaParserTests`
Expected: all 5 PASS (positions 1–3, the ≥150 count, the date and the PTS value all live in the first table).

- [ ] **Step 5: Commit**

```powershell
git add SportsRankingService/Parsers/FibaParser.cs SportsRankingService.Tests/Parsers/FibaParserTests.cs
git commit -m @'
Scope the FIBA parser to the main ranking table

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Remove the collapse — every entry stored, equal duplicates rejected

One compile unit: the snapshot entry record loses `RankedEntrants`, the builder loses the collapse and `Take`, `RankingItem` loses `Take`, and the hash, row entity and repository follow. All affected tests change in the same task because the record's constructor changes arity.

**Files:**
- Modify: `SportsRankingService/Services/RankingSnapshotEntry.cs`
- Modify: `SportsRankingService/Services/RankingSnapshotBuilder.cs`
- Modify: `SportsRankingService/Services/RankingSnapshot.cs` (doc comment only)
- Modify: `SportsRankingService/Models/RankingItem.cs`
- Modify: `SportsRankingService/Persistence/RankingContentHash.cs`
- Modify: `SportsRankingService/Persistence/RankingRow.cs`
- Modify: `SportsRankingService/Persistence/RankingRepository.cs`
- Test: `SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs`
- Test: `SportsRankingService.Tests/Persistence/RankingContentHashTests.cs`
- Test: `SportsRankingService.Tests/Persistence/RankingRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1's partner names appear in test data only; parsers are untouched here.
- Produces: `RankingSnapshotEntry(short Position, string ISO3, string? TeamName = null, string? Competitor = null, decimal? Points = null)` — sealed record, value equality. `RankingSnapshotBuilder.Build(RankingItem, ParsedRanking, DateOnly?, DateOnly)` — same signature, now throws `ParseException(item.Source, …)` on equal duplicates. `RankingItem` without `Take`. `RankingRow` without `RankedEntrants`. Tasks 4 and 6 rely on exactly these shapes.
- Untouched by design: `RankingSourceRunnerTests` — its fixture feeds (FIH 104, FIFA 211, FIG Group 5x 21) contain no same-country multiples, so their snapshot counts don't change (parser-level tests assert the same numbers).

- [ ] **Step 1: Rewrite the builder tests for store-everything semantics**

Replace the entire contents of `SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs` with:

```csharp
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

public class RankingSnapshotBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private static readonly RankingItem Item = new()
    {
        Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://x", Source = "Fih",
    };

    private static RankingSnapshot Build(params RankEntry[] entries) =>
        RankingSnapshotBuilder.Build(Item, new ParsedRanking(entries), null, Today);

    [Fact]
    public void Stamps_sport_event_and_gender_from_the_item()
    {
        RankingSnapshot snapshot = Build(new RankEntry(3, "DEU", "Germany"));

        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal([new RankingSnapshotEntry(3, "DEU", "Germany")], snapshot.Entries);
    }

    [Fact]
    public void Entries_come_out_in_position_order()
    {
        RankingSnapshot snapshot = Build(new(2, "BEL"), new(1, "AUS"), new(3, "CAN"));

        Assert.Equal(["AUS", "BEL", "CAN"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Entries_tied_on_position_keep_their_feed_order()
    {
        RankingSnapshot snapshot = Build(new(1, "KOR"), new(1, "DNK"), new(1, "CHN"));

        Assert.Equal(["KOR", "DNK", "CHN"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Every_entry_is_kept_a_country_may_appear_many_times()
    {
        RankingSnapshot snapshot = Build(
            new(1, "USA", Competitor: "Biles"),
            new(3, "USA", Competitor: "Lee"),
            new(2, "JPN", Competitor: "Okamura"),
            new(5, "USA", Competitor: "Jones"));

        Assert.Equal(["Biles", "Okamura", "Lee", "Jones"], snapshot.Entries.Select(e => e.Competitor));
        Assert.Equal(["USA", "JPN", "USA", "USA"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Partners_differing_only_by_competitor_are_both_kept()
    {
        RankingSnapshot snapshot = Build(
            new(1, "KOR", "Korea", "KIM Won Ho", 114099m),
            new(1, "KOR", "Korea", "SEO Seung Jae", 114099m));

        Assert.Equal(2, snapshot.Entries.Count);
    }

    [Fact]
    public void Equal_entries_fail_the_build_naming_the_entry()
    {
        var ex = Assert.Throws<ParseException>(() => Build(
            new(1, "KOR", "Korea", "KIM Won Ho", 114099m),
            new(1, "KOR", "Korea", "KIM Won Ho", 114099m)));

        Assert.Contains("KOR", ex.Message);
    }

    [Fact]
    public void The_country_name_is_filled_from_the_code_when_the_feed_gives_none()
    {
        RankingSnapshot snapshot = Build(new(1, "DEU"), new(2, "NLD", "Nederland"), new(3, "WI"));

        Assert.Equal(["Germany", "Nederland", null], snapshot.Entries.Select(e => e.TeamName));
    }

    [Fact]
    public void Competitor_and_points_are_carried_through()
    {
        RankingSnapshot snapshot = Build(new RankEntry(1, "ESP", "Spain", "Alcaraz", 11500m));

        RankingSnapshotEntry spain = Assert.Single(snapshot.Entries);
        Assert.Equal("Alcaraz", spain.Competitor);
        Assert.Equal(11500m, spain.Points);
    }

    [Fact]
    public void The_parsers_date_wins_and_is_flagged_as_the_federations()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")], new DateOnly(2026, 9, 12)), new DateOnly(2026, 9, 1), Today);

        Assert.Equal(new DateOnly(2026, 9, 12), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
    }

    [Fact]
    public void The_resolvers_date_is_used_when_the_parser_has_none()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")]), new DateOnly(2026, 9, 1), Today);

        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
    }

    [Fact]
    public void Today_is_the_fallback_and_is_flagged_as_not_the_federations()
    {
        RankingSnapshot snapshot = Build(new RankEntry(1, "DEU"));

        Assert.Equal(Today, snapshot.RankingDate);
        Assert.False(snapshot.IsFederationDate);
    }

    [Fact]
    public void Describe_joins_the_non_empty_parts()
    {
        RankingItem noEvent = new() { Sport = "Basketball", Gender = "Women", Url = "http://x", Source = "Fiba" };

        Assert.Equal("Field Hockey Outdoor Men", RankingSnapshotBuilder.Build(Item, new ParsedRanking([]), null, Today).Describe());
        Assert.Equal("Basketball Women", RankingSnapshotBuilder.Build(noEvent, new ParsedRanking([]), null, Today).Describe());
    }
}
```

(Gone: `One_row_per_country_keeps_the_best_placed_entry_and_counts_the_rest`, `A_country_tied_with_itself_keeps_its_first_feed_entry`, `Take_applies_by_position_after_the_country_collapse`.)

- [ ] **Step 2: Run the builder tests to verify the new ones fail**

Run: `dotnet test SportsRankingService.Tests --filter RankingSnapshotBuilderTests`
Expected: the file compiles against the old 6-parameter record (its optional parameters absorb the shorter constructor calls), and three tests FAIL: `Every_entry_is_kept_a_country_may_appear_many_times` and `Partners_differing_only_by_competitor_are_both_kept` (entries are still collapsed) and `Equal_entries_fail_the_build_naming_the_entry` (no exception is thrown). The rest pass.

- [ ] **Step 3: Change the record, the builder and the item**

Replace the contents of `SportsRankingService/Services/RankingSnapshotEntry.cs` with:

```csharp
namespace SportsRankingService.Services;

/// <summary>
/// One entry's row in a <see cref="RankingSnapshot"/>: a ranked athlete, doubles partner, group
/// or team. <paramref name="TeamName"/> is always the country name when one is known. Value
/// equality doubles as the builder's duplicate guard, so every stored fact is a positional
/// parameter of this record.
/// </summary>
public sealed record RankingSnapshotEntry(
    short Position,
    string ISO3,
    string? TeamName = null,
    string? Competitor = null,
    decimal? Points = null);
```

Replace the contents of `SportsRankingService/Services/RankingSnapshotBuilder.cs` with:

```csharp
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Services;

/// <summary>
/// Turns parser output into a <see cref="RankingSnapshot"/>: orders the entries by position,
/// fills a missing country name, rejects equal duplicates, stamps the item and decides the date.
/// </summary>
public static class RankingSnapshotBuilder
{
    public static RankingSnapshot Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today)
    {
        // The payload's own date is the most specific; a resolver's date came from the same federation's release list.
        DateOnly? federationDate = parsed.RankingDate ?? resolvedDate;

        // A stable sort, so entries tied on position keep feed order.
        List<RankingSnapshotEntry> entries = parsed.Entries
            .OrderBy(e => e.Position)
            .Select(e => new RankingSnapshotEntry(e.Position, e.ISO3, e.TeamName ?? CountryUtil.GetCountryName(e.ISO3), e.Competitor, e.Points))
            .ToList();

        // Equal rows carry no distinguishing fact, so they are always a feed or parser bug:
        // distinct entities differ at least in competitor (see the 2026-09-17 spec).
        HashSet<RankingSnapshotEntry> seen = [];
        foreach (RankingSnapshotEntry entry in entries)
        {
            if (!seen.Add(entry))
            {
                throw new ParseException(item.Source, $"duplicate entry {entry}");
            }
        }

        return new RankingSnapshot(
            item.Sport,
            item.Event,
            item.Gender,
            federationDate ?? today,
            IsFederationDate: federationDate is not null,
            entries);
    }
}
```

In `SportsRankingService/Models/RankingItem.cs`, delete the `Take` property and its XML doc (the two lines starting `/// <summary>Keep every entry whose position is ≤ N …` and `public int? Take { get; set; }`).

In `SportsRankingService/Services/RankingSnapshot.cs`, change the doc phrase "and one entry per country in position order" to "and its entries in position order".

- [ ] **Step 4: Follow the ripple through hash, row and repository**

`SportsRankingService/Persistence/RankingContentHash.cs` — the class doc's quoted line becomes "position, ISO3, team name, competitor, points"; the loop body becomes:

```csharp
text.Append(entry.Position.ToString(CultureInfo.InvariantCulture)).Append('\t')
    .Append(entry.ISO3).Append('\t')
    .Append(entry.TeamName).Append('\t')
    .Append(entry.Competitor).Append('\t')
    .Append(entry.Points?.ToString(CultureInfo.InvariantCulture)).Append('\n');
```

`SportsRankingService/Persistence/RankingRow.cs` — delete the `RankedEntrants` property and its doc; replace the class doc with:

```csharp
/// <summary>
/// One ranked entry's row in a <see cref="RankingRelease"/>. Ordinal is the storage order (position
/// order, ties by feed order); positions can repeat and a country can appear many times.
/// </summary>
```

and change the `Position` doc to `/// <summary>The federation's published position of this entry.</summary>`.

`SportsRankingService/Persistence/RankingRepository.cs` — delete the line `RankedEntrants = entry.RankedEntrants,`.

- [ ] **Step 5: Update the hash and repository tests**

`RankingContentHashTests.cs`: replace `Changes_when_the_competitor_points_or_entrants_change` with:

```csharp
[Fact]
public void Changes_when_the_competitor_or_points_change()
{
    RankingSnapshot baseline = Snapshot(new(2026, 9, 12), true, new RankingSnapshotEntry(1, "DEU", "Germany", "Anna", 100m));

    Assert.NotEqual(RankingContentHash.Compute(baseline), RankingContentHash.Compute(Snapshot(new(2026, 9, 12), true, new RankingSnapshotEntry(1, "DEU", "Germany", "Berta", 100m))));
    Assert.NotEqual(RankingContentHash.Compute(baseline), RankingContentHash.Compute(Snapshot(new(2026, 9, 12), true, new RankingSnapshotEntry(1, "DEU", "Germany", "Anna", 101m))));
}
```

`RankingRepositoryTests.cs`: replace `Two_entries_at_the_same_position_are_both_stored` with:

```csharp
[Fact]
public async Task Two_entries_at_the_same_position_are_both_stored()
{
    RankingSnapshot doubles = new("Badminton", "Doubles", "Men", new(2026, 9, 15), false,
        [new(1, "KOR", "Korea", "KIM Won Ho"), new(1, "KOR", "Korea", "SEO Seung Jae")]);

    await SaveAsync(doubles);

    RankingRelease release = Assert.Single(Releases());
    Assert.Equal(["KIM Won Ho", "SEO Seung Jae"], release.Rows.Select(x => x.Competitor));
    Assert.All(release.Rows, x => Assert.Equal(1, x.Position));
}
```

and replace `Stores_the_country_name_competitor_points_and_entrants` with:

```csharp
[Fact]
public async Task Stores_the_country_name_competitor_and_points()
{
    await SaveAsync(new RankingSnapshot("Tennis", "Singles", "Men", new(2026, 9, 10), true,
        [new(1, "ITA", "Italy", "Jannik Sinner", 11500m), new(2, "ESP", "Spain", "Carlos Alcaraz", 9000.5m)]));

    RankingRelease release = Assert.Single(Releases());
    Assert.Equal(
        [("ITA", "Italy", "Jannik Sinner", 11500m), ("ESP", "Spain", "Carlos Alcaraz", 9000.5m)],
        release.Rows.Select(x => (x.ISO3, x.TeamName, x.Competitor, x.Points)));
}
```

No other test in either file passes `RankedEntrants` (the remaining constructors use ≤5 arguments already).

- [ ] **Step 6: Build, run everything, verify green**

Run: `dotnet build SportsRankingService.sln`
Expected: 0 errors. If `RankedEntrants` or `Take` still appears anywhere outside `Persistence/Migrations/` (regenerated in Task 4), the build tells you where.

Run: `dotnet test SportsRankingService.Tests`
Expected: all green, 3 skipped. `RankingSourceRunnerTests` pass unchanged (FIH 104, FIFA 211, FIG 21 — duplicate-free fixtures).

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m @'
Store every parsed entry and reject equal duplicates

The builder no longer collapses to countries; RankedEntrants and the
unused Take are gone, and record equality guards against a feed or
parser emitting the same entry twice.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Regenerate the migration; derive countries in a second view

Nothing is deployed, so the single `InitialSchema` migration is regenerated (same precedent as the country-rows change) and the dev database recreated. `CurrentRankings` gains `Ordinal` (row identity and stable order for consumers) and loses `RankedEntrants`; the new `CurrentCountryRankings` derives the old country shape: `MIN(Ordinal)` per (release, country) IS the best-placed entry because `Ordinal` is assigned after the stable position sort.

**Files:**
- Delete: `SportsRankingService/Persistence/Migrations/20260917022217_InitialSchema.cs`, `…InitialSchema.Designer.cs`, `RankingsDbContextModelSnapshot.cs`
- Create (generated): `SportsRankingService/Persistence/Migrations/<newstamp>_InitialSchema.cs` + Designer + snapshot
- Modify: the generated migration (view SQL added by hand)

**Interfaces:**
- Consumes: Task 3's `RankingRow` without `RankedEntrants`.
- Produces: `dbo.CurrentRankings(Sport, Event, Gender, RankingDate, IsFederationDate, Ordinal, Position, ISO3, TeamName, Competitor, Points)` and `dbo.CurrentCountryRankings(Sport, Event, Gender, RankingDate, IsFederationDate, Position, ISO3, TeamName, Competitor, Points, RankedEntrants)`. Task 6's doc updates name both.

- [ ] **Step 1: Regenerate the migration**

```powershell
Remove-Item SportsRankingService/Persistence/Migrations/* -Force
dotnet ef migrations add InitialSchema --project SportsRankingService --startup-project SportsRankingService --output-dir Persistence/Migrations
```

Expected: three new files; the generated `Up` has no `RankedEntrants` column.

- [ ] **Step 2: Add the two views to the generated migration**

In the new `*_InitialSchema.cs`, after the `CreateIndex` call in `Up`, add:

```csharp
// Read models for consumers, raw SQL outside the EF model; a later migration that
// changes a projected column must drop and re-create them itself.
// CurrentRankings: every row of the newest release per feed.
migrationBuilder.Sql("""
    CREATE VIEW dbo.CurrentRankings AS
    SELECT r.Sport, r.Event, r.Gender, r.RankingDate, r.IsFederationDate,
           x.Ordinal, x.Position, x.ISO3, x.TeamName, x.Competitor, x.Points
    FROM dbo.RankingReleases r
    JOIN dbo.RankingRows x ON x.ReleaseId = r.Id
    WHERE r.Id = (
        SELECT MAX(n.Id)
        FROM dbo.RankingReleases n
        WHERE n.Sport = r.Sport
          AND n.Gender = r.Gender
          AND (n.Event = r.Event OR (n.Event IS NULL AND r.Event IS NULL)));
    """);

// CurrentCountryRankings: one row per country per feed — its best-placed entry plus how many
// entries the country holds. MIN(Ordinal) is the best entry because Ordinal is assigned
// after the stable position sort (ties resolved by feed order).
migrationBuilder.Sql("""
    CREATE VIEW dbo.CurrentCountryRankings AS
    SELECT r.Sport, r.Event, r.Gender, r.RankingDate, r.IsFederationDate,
           x.Position, x.ISO3, x.TeamName, x.Competitor, x.Points, c.RankedEntrants
    FROM dbo.RankingReleases r
    JOIN dbo.RankingRows x ON x.ReleaseId = r.Id
    JOIN (
        SELECT ReleaseId, ISO3, COUNT(*) AS RankedEntrants, MIN(Ordinal) AS BestOrdinal
        FROM dbo.RankingRows
        GROUP BY ReleaseId, ISO3
    ) c ON c.ReleaseId = x.ReleaseId AND c.ISO3 = x.ISO3 AND c.BestOrdinal = x.Ordinal
    WHERE r.Id = (
        SELECT MAX(n.Id)
        FROM dbo.RankingReleases n
        WHERE n.Sport = r.Sport
          AND n.Gender = r.Gender
          AND (n.Event = r.Event OR (n.Event IS NULL AND r.Event IS NULL)));
    """);
```

and at the top of `Down`, before the `DropTable` calls:

```csharp
migrationBuilder.Sql("DROP VIEW dbo.CurrentCountryRankings;");
migrationBuilder.Sql("DROP VIEW dbo.CurrentRankings;");
```

- [ ] **Step 3: Recreate the database and apply**

```powershell
docker compose down -v
docker compose up -d --wait
dotnet ef database update --project SportsRankingService --startup-project SportsRankingService
```

Expected: update applies cleanly (both views created).

- [ ] **Step 4: Run the app live and verify the views**

Run: `dotnet run --project SportsRankingService; $LASTEXITCODE`
Expected: exit code 0, 47 feeds, all `Inserted` (fresh DB). A transient live-site failure is not a task failure — re-run once; only a *parse* error (which would name a feed) needs investigation.

```powershell
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Rankings_Dev1!' -d WorldRankGuesser -Q "SELECT (SELECT COUNT(*) FROM dbo.CurrentRankings) AS EntryRows, (SELECT COUNT(*) FROM dbo.CurrentCountryRankings) AS CountryRows;"
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Rankings_Dev1!' -d WorldRankGuesser -Q "SELECT TOP 5 Position, ISO3, Competitor, RankedEntrants FROM dbo.CurrentCountryRankings WHERE Sport='Tennis' AND Gender='Men' ORDER BY Position;"
```

Expected: `EntryRows` clearly above the old ~2,663 (tennis and gymnastics now store every athlete); `CountryRows` in the same ballpark as the old figure (~2,600–2,700). The tennis spot-check shows real athlete names with `RankedEntrants` > 1 for big tennis nations. Record the printed `EntryRows` number — Task 6 writes it into CLAUDE.md.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m @'
Regenerate the schema for entry rows and derive countries in a view

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Fetch full volleyball lists; document the WTA and BWF caps

Live probes (2026-09-17): Volleyball World serves at most 500 rows (600+ errors); indoor is complete at that size (148 men, 133 women), beach fills all 500. WTA silently caps `pageSize` at 100. BWF honors `pageKey=2000` (already noted in FIXTURES.md).

**Files:**
- Modify: `SportsRankingService/serviceconfig.json`
- Modify: `SportsRankingService.Tests/Fixtures/VolleyballWorld_Men.json`, `…/VolleyballWorld_Beach_Men.json` (recaptured)
- Modify: `SportsRankingService.Tests/Fixtures/FIXTURES.md`
- Test: `SportsRankingService.Tests/Parsers/VolleyballWorldParserTests.cs`

**Interfaces:**
- Produces: config URLs Task 6's final live run fetches. No code signatures change.

- [ ] **Step 1: Update serviceconfig.json**

The four volleyball URLs change their trailing `/100` to `/500` (men indoor `volleyball/1/0/500`, women indoor `volleyball/0/0/500`, beach men `beachvolleyball/0/0/500`, beach women `beachvolleyball/1/0/500`). The five BWF URLs change `pageKey=150` to `pageKey=2000`. The WTA item gains, after its `Source` property:

```json
"Note": "The API caps pageSize at 100 whatever is asked (checked 2026-09-17); positions past 100 need page=1 and up, which waits for the multi-page fetch spec.",
```

- [ ] **Step 2: Recapture the two volleyball fixtures**

```powershell
$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36'
curl.exe -s -A $ua 'https://en.volleyballworld.com/api/v1/worldranking/volleyball/1/0/500' -o SportsRankingService.Tests/Fixtures/VolleyballWorld_Men.json
curl.exe -s -A $ua 'https://en.volleyballworld.com/api/v1/worldranking/beachvolleyball/0/0/500' -o SportsRankingService.Tests/Fixtures/VolleyballWorld_Beach_Men.json
```

Then print the values the tests will assert:

```powershell
$m = (Get-Content SportsRankingService.Tests/Fixtures/VolleyballWorld_Men.json -Raw | ConvertFrom-Json).teams
"indoor: count=$($m.Count) first=$($m[0].federationCode) pts=$($m[0].decimalPoints) last=$($m[-1].federationCode)@$($m[-1].rankToDisplay)"
$b = (Get-Content SportsRankingService.Tests/Fixtures/VolleyballWorld_Beach_Men.json -Raw | ConvertFrom-Json).teams
"beach: count=$($b.Count) first=$($b[0].federationCode) '$($b[0].name)' pts=$($b[0].points)"
```

On 2026-09-17 this printed: indoor count 148, POL first with 400.16, NZL last at 148; beach count 500, SWE first, pair "Hölting Nilsson/Andersson, E", 8020 points. If your capture prints different values (the feed updates roughly weekly), use the printed values in Step 3 instead — that is updating expectations to the captured file, not weakening assertions.

- [ ] **Step 3: Update the volleyball tests to the captured values**

In `VolleyballWorldParserTests.cs`, `Parses_every_ranked_team` becomes (values from Step 2's printout):

```csharp
[Fact]
public void Parses_every_ranked_team()
{
    var rows = _parser.Parse(Fixture.Read("VolleyballWorld_Men.json")).Entries;

    Assert.Equal(148, rows.Count);
    Assert.Equal("POL", rows.Single(r => r.Position == 1).ISO3);
    Assert.Equal("NZL", rows.Single(r => r.Position == 148).ISO3);
    Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
}
```

`Carries_the_decimal_points` asserts `400.16m` (or the printed value). In `Beach_ranks_pairs_with_the_pair_as_competitor_and_integer_points`, add as the first assertion `Assert.Equal(500, rows.Count);` and keep the SWE top-pair assertions with the printed name and points.

- [ ] **Step 4: Update FIXTURES.md**

Delete the stray first line (the `VolleyballWorld_Beach_Men.json | …` table row sitting above the `# Fixtures` heading). In the main table, update the `VolleyballWorld_Men.json` row's URL to `…/volleyball/1/0/500` and append `; recaptured 2026-09-17 at the API's 500-row maximum (148 teams, the full list)`. Add below it a proper row:

```markdown
| VolleyballWorld_Beach_Men.json | https://en.volleyballworld.com/api/v1/worldranking/beachvolleyball/0/0/500 | recaptured 2026-09-17; a pair per row (`player1Name`, `name`), integer `points`; 500 rows is the API's per-request maximum and the beach list is deeper (multi-page spec) |
```

In the BWF caveat paragraph at the bottom, append: "serviceconfig.json now asks for `pageKey=2000` (honored per the note above); the fixtures remain the `pageKey=100` captures."

- [ ] **Step 5: Run the tests, then the full suite**

Run: `dotnet test SportsRankingService.Tests --filter VolleyballWorldParserTests`
Expected: all PASS against the recaptured files.

Run: `dotnet test SportsRankingService.Tests`
Expected: all green, 3 skipped.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m @'
Fetch the full volleyball lists and record the WTA page cap

Volleyball World serves at most 500 rows per request: indoor is
complete at that size, beach fills it. WTA caps pageSize at 100, so
its depth waits for multi-page fetching. BWF asks for pageKey=2000
for when Cloudflare lets .NET back in.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 6: Documentation and final verification

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the `EntryRows` count recorded in Task 4 Step 4 (re-query it here after the second run if you skipped noting it).

- [ ] **Step 1: Update CLAUDE.md**

Make these edits (each is old phrase → new phrase within the named section):

1. **What this is:** "normalizes each feed to a `RankingSnapshot` (sport / event / gender / ranking date / entries of position + ISO3 country code)" → "normalizes each feed to a `RankingSnapshot` (sport / event / gender / ranking date / one entry per ranked athlete, pair partner or team: position, ISO3, competitor, points)".
2. **Persistence:** "`RankingRows` (`ReleaseId`, `Ordinal`, `Position`, `ISO3` varchar(3), `TeamName`, `Competitor`, `Points` decimal(12,3), `RankedEntrants`), one row per country per release" → "`RankingRows` (`ReleaseId`, `Ordinal`, `Position`, `ISO3` varchar(3), `TeamName`, `Competitor`, `Points` decimal(12,3)), one row per parsed entry per release".
3. **Persistence:** "The `CurrentRankings` view (raw SQL in the initial migration, not mapped in EF) exposes the newest release per feed for the planned consumer app." → "Two views (raw SQL in the initial migration, not mapped in EF) serve the planned consumer app: `CurrentRankings` exposes every row of the newest release per feed, and `CurrentCountryRankings` collapses it to one row per country — the best-placed entry (`MIN(Ordinal)`) plus `RankedEntrants = COUNT(*)` of the country's rows." Also change the later sentence "a later migration that renames or retypes a column it projects must drop and re-create the view itself" to "…must drop and re-create the views themselves".
4. **Architecture step 3:** "builds a `RankingSnapshot` via `RankingSnapshotBuilder`: entries ordered by position, collapsed to one `RankingSnapshotEntry` per country (the best-placed entry wins, ties by feed order; `RankedEntrants` counts the country's entries in the whole ranking), a missing `TeamName` filled from `CountryUtil.GetCountryName`, `Take` applied by position, and the ranking date chosen" → "builds a `RankingSnapshot` via `RankingSnapshotBuilder`: every entry kept and ordered by position (ties keep feed order), a missing `TeamName` filled from `CountryUtil.GetCountryName`, equal duplicate entries rejected with `ParseException` (record equality is the guard), and the ranking date chosen".
5. **Parsers paragraph:** "A parser emits one entry per ranked entity; the builder collapses them to countries, so every stored ranking is a country ranking whatever the federation ranks (see `docs/superpowers/specs/2026-09-16-country-rows-design.md`)." → "A parser emits one entry per ranked entity and the builder stores them all; countries are derived in the `CurrentCountryRankings` view (see `docs/superpowers/specs/2026-09-17-store-all-entries-design.md`)."
6. **Country handling:** "BWF doubles pairs produce one row per partner with the pair's position" → "BWF doubles pairs produce one row per partner with the pair's position and points, each row naming its own partner".
7. **Current state:** rewrite the paragraph's row-shape sentence: "Rows became one-per-country with `Competitor`, `Points` and `RankedEntrants` the same day (spec: `docs/superpowers/specs/2026-09-16-country-rows-design.md`); the `InitialSchema` migration was regenerated for it because nothing had been deployed." → "Rows became one-per-country on 2026-09-16, then one-per-entry on 2026-09-17 with countries derived in `CurrentCountryRankings` (spec: `docs/superpowers/specs/2026-09-17-store-all-entries-design.md`); the `InitialSchema` migration was regenerated each time because nothing had been deployed." Update the opening feed line's row count "about 2,700 country rows per run" to "about N entry rows per run" using the `EntryRows` number from Task 4 Step 4 (write the actual number, e.g. "about 4,300 entry rows"). Append to the paragraph: "Volleyball World fetches 500 rows per feed (the API's per-request maximum; indoor complete, beach truncated), WTA caps `pageSize` at 100, and ESPN has no size parameter — deeper lists for those feeds wait for a multi-page fetch spec."
8. **Persistence (hash sentence):** "hashes the snapshot (every stored column of every row plus the federation date; a scrape date is excluded)" is still literally true — leave it.

- [ ] **Step 2: Update README.md**

Line 37: "`CurrentRankings` is a view with one row per entry of each feed's newest release" → "`CurrentRankings` is a view with one row per entry of each feed's newest release; `CurrentCountryRankings` collapses it to one row per country (the best-placed entry plus its entrant count)".

- [ ] **Step 3: Final verification**

```powershell
dotnet build SportsRankingService.sln
dotnet test SportsRankingService.Tests
dotnet run --project SportsRankingService; $LASTEXITCODE
```

Expected: 0 errors; all tests green (3 skipped); exit code 0. On this second run the four volleyball feeds report `Inserted` (their content grew to 500/148/133 rows) and stable feeds report `Unchanged` — which also proves insert-on-change still works against the new schema.

- [ ] **Step 4: Commit**

```powershell
git add CLAUDE.md README.md
git commit -m @'
Document the entry-row storage model

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
