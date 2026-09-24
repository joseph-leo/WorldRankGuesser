# Synthetic Samples and History Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop distributing copied federation pages and their embedded keys: replace every captured response in the scraper's tests with a synthetic sample the project owns, keep real captures as an optional local-only check, and remove the captured files and the 2023 Sportradar key from the whole git history of the public repository.

**Architecture:** `tests/SportsRankingService.Tests/Samples/` holds one small hand-shaped response per feed, written by a committed Node script (`generate.mjs`) so every shape lives in one readable place; parser and resolver tests assert against the samples. `Captures/` (git-ignored) may hold real responses under the same file names; `CaptureTests` runs every parser over them when the folder exists and is skipped otherwise. The history rewrite is `git filter-branch` (no Python or Java on this machine), dropping the three scraped folders from every commit and redacting the Sportradar key in the two files that carried it, followed by a force push with the `main` ruleset briefly disabled.

**Tech Stack:** .NET 11 / xUnit 2.9.3 (no dynamic skip: a `TheoryAttribute` subclass sets `Skip` in its constructor), HtmlAgilityPack, Node 24 (already a dev dependency of the web project), git 2.54 `filter-branch`, `gh` CLI, GitHub secret-scanning and rulesets REST APIs.

**Spec:** This conversation's decision (2026-09-23): option 2 (synthetic fixtures) plus a clean history. Background: `docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md` section 8.8 (going public) and `src/SportsRankingService/CLAUDE.md` (tests are fixture-based; parsers are pure functions of the response).

## Global Constraints

- No third-party page content in the repository: no copied HTML, scripts, prose, copyright notices or keys. Samples carry only country codes and names (facts), invented athlete names (the NATO alphabet as surnames), invented points and dates the tests already pin.
- Every sample keeps the exact shape its parser or resolver reads (property names, `data-label` cells, `__NEXT_DATA__`, `data-series-ids`, `{"date","sport"}` objects), and nothing else.
- Every code a sample yields must have a name in `CountryNames` (the builder fails a feed otherwise); the theory over `Feeds.All` enforces it.
- Tests keep the quirk each one pins (tied pairs, echoed rows, `-W` suffixes, `NR`/`new` rows, unranked FIFA teams, movers tables, the tied bottom of WBSC); only the numbers and names change.
- The scraper never references the test project; `tools/` and `src/` contain no `Fixtures` reference after this plan (`grep -rn Fixtures src tools` is empty).
- Never print the Sportradar key: read it into a shell variable from the historical file and use the variable.
- `main` accepts only pull requests with green CI (ruleset 23907343: `deletion`, `non_fast_forward`, `pull_request`, `required_status_checks`); the rewrite disables and re-enables that ruleset around one force push.
- The rewrite happens only after the samples PR is merged, on a clean tree, after `git bundle` has saved every ref outside the repository.

## Review Focus

1. A capture folder that exists but is missing one file: `CaptureTests` must fail that one case with the file name, not skip silently. Pinned in Task 4 (`Capture.Read` throws `FileNotFoundException`, and the theory has no try/catch).
2. A sample whose country code has no name in `CountryNames`: must fail the `Feeds.All` theory naming the code, exactly as the old fixture test did. Pinned in Task 1.
3. The FIG selector theory still walks every series × apparatus on every page, so a table the generator forgot fails as "no 'X' tab" rather than passing vacuously. Pinned in Task 3 (`EveryTable` unchanged, minimum rows lowered to the sample size).
4. The rewrite must leave no reachable commit containing `Fixtures/`, `remotehtml/`, a Google `AIza` key or the Sportradar key, and every local branch and the `scraper-net8-baseline` tag must be rewritten too. Pinned in Task 6 by four verification commands that must print `0`, and a `gitleaks` run over `--all` that must exit 0.
5. The force push must not delete or rewrite anything on the remote other than `main` and the tag: only those two refs are pushed, never `--all`. Task 6 pushes them by name.

---

### Task 1: The sample generator, the loaders and the country-name theory

**Files:**
- Create: `tests/SportsRankingService.Tests/Samples/generate.mjs`
- Create: `tests/SportsRankingService.Tests/Samples/SAMPLES.md`
- Create: `tests/SportsRankingService.Tests/Samples.cs` (replaces `Fixture.cs`)
- Create: `tests/SportsRankingService.Tests/Feeds.cs`
- Create: `tests/SportsRankingService.Tests/Utilities/SampleCountryCodesTests.cs` (replaces `FixtureCountryCodesTests.cs`)
- Delete: `tests/SportsRankingService.Tests/Fixture.cs`, `tests/SportsRankingService.Tests/Utilities/FixtureCountryCodesTests.cs`, `tests/SportsRankingService.Tests/Fixtures/` (every file; the real captures move to the ignored `Captures/` folder first)
- Modify: `tests/SportsRankingService.Tests/SportsRankingService.Tests.csproj:26`
- Modify: `.gitignore` (append)

**Interfaces:**
- Produces: `Sample.Read(string fileName) : string`; `Capture.Directory : string`, `Capture.Available : bool`, `Capture.Read(string fileName) : string`; `CaptureTheoryAttribute : TheoryAttribute`; `Feeds.All() : TheoryData<IRankingParser, string, string?>` (parser, sample file name, selector), the one list of every feed shape; the sample file names below, which Tasks 2 and 3 read.

Sample file names (unchanged from the fixtures except the two FIFA lists): `Bwf_MensSingles.json`, `Bwf_MensDoubles.json`, `Bwf_MensSingles_RepeatedRows.json`, `Bwf_MensDoubles_TiedPairs.json`, `Espn_Atp_Singles.json`, `Fiba_Ranking_Men.html`, `Fifa_V3_Men.json`, `Fifa_V3_Women.json`, `Fifa_WorldRanking_Men.html`, `Fifa_WorldRanking_Women.html`, `Fig_Artistic_Men.html`, `Fig_Artistic_Women.html`, `Fig_Rhythmic_Women.html`, `Fih_Outdoor_Men.json`, `Icc_T20_Men.json`, `Icc_T20_Women.json`, `Icc_Test_Men.json`, `Svns_Series_Men.json`, `Svns_Series_Women.json`, `Svns_Standings.html`, `Svns_Standings_Men.json`, `Svns_Standings_Women.json`, `VolleyballWorld_Beach_Men.json`, `VolleyballWorld_Men.json`, `Wbsc_Baseball_Men.json`, `Wbsc_Rankings.html`, `Wikipedia_IihfWorldRanking.html`, `WorldRugby_Union_Men.json`, `Wta_Doubles.json`. `Fifa_Overview_Men_id14870.json` (unused) is dropped.

- [ ] **Step 1: Keep the real captures locally, out of git**

```bash
cd tests/SportsRankingService.Tests
mkdir -p Captures
mv Fixtures/*.html Fixtures/*.json Captures/     # plain mv: git must never track them
git rm -r -q Fixtures
printf '\n# Real federation responses for the optional capture pass (never committed)\ntests/SportsRankingService.Tests/Captures/\n' >> ../../.gitignore
git status --short | head   # Captures/ must not appear; Fixtures/* show as D
```

- [ ] **Step 2: Write the failing theory**

`tests/SportsRankingService.Tests/Feeds.cs`:

```csharp
using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests;

/// <summary>
/// Every feed shape the scraper parses: the parser, the sample file in Samples/ (and, when present, the
/// real capture of the same name in Captures/), and the selector for a page that holds several rankings.
/// </summary>
internal static class Feeds
{
    public static TheoryData<IRankingParser, string, string?> All()
    {
        TheoryData<IRankingParser, string, string?> data = new()
        {
            { new BwfParser(), "Bwf_MensDoubles.json", null },
            { new BwfParser(), "Bwf_MensSingles.json", null },
            { new EspnTennisParser(), "Espn_Atp_Singles.json", null },
            { new FibaParser(), "Fiba_Ranking_Men.html", null },
            { new FifaV3Parser(), "Fifa_V3_Men.json", null },
            { new FifaV3Parser(), "Fifa_V3_Women.json", null },
            { new FihParser(), "Fih_Outdoor_Men.json", null },
            { new IccParser(), "Icc_T20_Men.json", null },
            { new IccParser(), "Icc_T20_Women.json", null },
            { new IccParser(), "Icc_Test_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Men.json", null },
            { new SvnsParser(), "Svns_Standings_Women.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Beach_Men.json", null },
            { new VolleyballWorldParser(), "VolleyballWorld_Men.json", null },
            { new WbscParser(), "Wbsc_Baseball_Men.json", null },
            { new WikipediaIihfParser(), "Wikipedia_IihfWorldRanking.html", "Men" },
            { new WikipediaIihfParser(), "Wikipedia_IihfWorldRanking.html", "Women" },
            { new WorldRugbyParser(), "WorldRugby_Union_Men.json", null },
            { new WtaParser(), "Wta_Doubles.json", null },
        };

        foreach ((string page, string[] apparatus) in FigPages)
        {
            foreach (string series in FigSeries)
            {
                foreach (string a in apparatus)
                {
                    data.Add(new FigParser(), page, $"{series} / {a}");
                }
            }
        }

        return data;
    }

    public static readonly string[] FigSeries = ["World Cup", "World Challenge Cup"];

    /// <summary>Each FIG page and its apparatus tabs; every series has a tab per apparatus.</summary>
    public static readonly (string Page, string[] Apparatus)[] FigPages =
    [
        ("Fig_Artistic_Men.html", ["Floor Exercise", "Pommel Horse", "Still Rings", "Vault", "Parallel Bars", "Horizontal Bar"]),
        ("Fig_Artistic_Women.html", ["Vault", "Uneven Bars", "Balance Beam", "Floor Exercise"]),
        ("Fig_Rhythmic_Women.html", ["Individual All-Around", "Hoop", "Ball", "Clubs", "Ribbon", "Group All-Around", "Group 5x", "Group 3x+2x"]),
    ];
}
```

`tests/SportsRankingService.Tests/Utilities/SampleCountryCodesTests.cs`:

```csharp
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests.Utilities;

/// <summary>
/// Every code a parser emits from a sample must have a name in <see cref="CountryNames"/>, otherwise the
/// builder fails that feed at run time. Failing here names the codes to map (usually a federation-only
/// code for the IOC table) before a live run finds them one feed at a time.
/// </summary>
public class SampleCountryCodesTests
{
    [Theory]
    [MemberData(nameof(Feeds.All), MemberType = typeof(Feeds))]
    public void Every_code_a_sample_yields_has_a_country_name(IRankingParser parser, string sample, string? selector)
    {
        IEnumerable<string> unnamed = parser.Parse(Sample.Read(sample), selector).Entries
            .Select(e => e.ISO3)
            .Distinct()
            .Where(code => !CountryUtil.TryGetCountryName(code, out _))
            .Order();

        Assert.True(!unnamed.Any(), $"{sample}: no country name for {string.Join(", ", unnamed)}");
    }
}
```

`tests/SportsRankingService.Tests/Samples.cs`:

```csharp
namespace SportsRankingService.Tests;

/// <summary>A synthetic response from Samples/, written by Samples/generate.mjs; see Samples/SAMPLES.md.</summary>
internal static class Sample
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName));
}

/// <summary>
/// A real federation response saved under Captures/ with the same name as its sample. The folder is
/// git-ignored and optional: CaptureTests run when it exists next to the test binaries and are skipped otherwise.
/// </summary>
internal static class Capture
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Captures");

    public static bool Available => System.IO.Directory.Exists(Directory);

    public static string Read(string fileName) => File.ReadAllText(Path.Combine(Directory, fileName));
}

/// <summary>A theory that runs only when the Captures folder exists.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CaptureTheoryAttribute : TheoryAttribute
{
    public CaptureTheoryAttribute()
    {
        if (!Capture.Available)
        {
            Skip = "No Captures folder next to the test binaries; see Samples/SAMPLES.md for how to capture real responses.";
        }
    }
}
```

Delete `Fixture.cs` and `Utilities/FixtureCountryCodesTests.cs`. In the csproj replace line 26 with:

```xml
    <None Include="Samples\**;Captures\**" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3: Run the theory to see it fail**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~SampleCountryCodesTests"`
Expected: build errors in the other test files (`Fixture` no longer exists) — that is Tasks 2 and 3. To see this theory alone fail, temporarily run with the rest excluded is not possible; instead do a global `sed -i 's/Fixture\.Read(/Sample.Read(/g'` over `tests/SportsRankingService.Tests/**/*.cs` now (the loader rename is mechanical; the expectation changes come in Tasks 2 and 3), build, and run the filter above. Expected: every case FAILS with `FileNotFoundException` naming a file under `Samples\`.

- [ ] **Step 4: Write the generator**

`tests/SportsRankingService.Tests/Samples/generate.mjs` (run with `node tests/SportsRankingService.Tests/Samples/generate.mjs`; it writes into its own folder):

```js
// Writes one synthetic response per feed into this folder. Everything here is invented except country
// codes and names; athlete surnames are the NATO alphabet. Keep each file to the shape its parser reads.
// Run: node tests/SportsRankingService.Tests/Samples/generate.mjs
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const write = (name, text) => writeFileSync(join(here, name), text.endsWith('\n') ? text : text + '\n');
const json = (value) => JSON.stringify(value, null, 2);

const NAMES = ['Jonah ALPHA', 'Mika BRAVO', 'Sacha CHARLIE', 'Rene DELTA', 'Toni ECHO', 'Kim FOXTROT', 'Ari GOLF', 'Lou HOTEL',
  'Sam INDIA', 'Noa JULIET', 'Eli KILO', 'Max LIMA', 'Ola MIKE', 'Uma NOVEMBER', 'Ivo OSCAR', 'Zoe PAPA', 'Rae QUEBEC', 'Ash ROMEO', 'Ida SIERRA', 'Bo TANGO'];
const name = (i) => NAMES[i % NAMES.length];
const given = (i) => name(i).split(' ')[0];
const surname = (i) => name(i).split(' ')[1];
const titleCase = (i) => `${given(i)} ${surname(i)[0]}${surname(i).slice(1).toLowerCase()}`;   // "Jonah Alpha"

// ---- BWF: Laravel envelope, countries as names, names as HTML ----------------------------------------------
const bwfName = (i) => `<span class="name-1">${given(i)}</span> <span class="name-2">${surname(i)}</span>`;
const bwfRow = (id, rank, points, p1, p2 = null) => ({
  id, rank, points: `${points}.0000`,
  player1_id: p1.id, player2_id: p2 ? p2.id : null,
  player1_model: { slug: `player-${p1.id}`, name_display_bold: bwfName(p1.n) },
  player2_model: p2 ? { slug: `player-${p2.id}`, name_display_bold: bwfName(p2.n) } : null,
  p1_country_model: { name: p1.country }, p2_country_model: p2 ? { name: p2.country } : null,
});
const bwf = (rows) => json({ results: { data: rows, total: rows.length, per_page: rows.length, last_page: 1 } });

const singlesCountries = ['INDONESIA', 'DENMARK', 'CHINA', 'JAPAN', 'THAILAND', 'MALAYSIA', 'INDIA', 'FRANCE', 'SINGAPORE', 'Korea', 'HONG KONG CHINA', 'AZERBAIJAN'];
write('Bwf_MensSingles.json', bwf(singlesCountries.map((country, i) =>
  bwfRow(100 + i, i + 1, 87631 - i * 3000, { id: 1000 + i, n: i, country }))));

const doublesPairs = [
  ['Korea', 'Korea'], ['DENMARK', 'DENMARK'], ['MALAYSIA', 'MALAYSIA'], ['INDONESIA', 'INDONESIA'], ['Korea', 'MALAYSIA'],
  ['JAPAN', 'JAPAN'], ['CHINA', 'CHINA'], ['Athlete Independent Neutral', 'Athlete Independent Neutral'], ['INDIA', 'INDIA'], ['FRANCE', 'FRANCE'],
];
write('Bwf_MensDoubles.json', bwf(doublesPairs.map(([c1, c2], i) =>
  bwfRow(200 + i, i + 1, 114099 - i * 4000, { id: 2000 + 2 * i, n: 2 * i, country: c1 }, { id: 2001 + 2 * i, n: 2 * i + 1, country: c2 }))));

// Five consecutive rows tied at rank 925: three players, two of them listed twice (a new row id, the same player id).
write('Bwf_MensSingles_RepeatedRows.json', bwf([
  bwfRow(901, 925, 300, { id: 51, n: 0, country: 'PERU' }),
  bwfRow(902, 925, 300, { id: 51, n: 0, country: 'PERU' }),
  bwfRow(903, 925, 300, { id: 52, n: 1, country: 'DOMINICAN REPUBLIC' }),
  bwfRow(904, 925, 300, { id: 53, n: 2, country: 'CANADA' }),
  bwfRow(905, 925, 300, { id: 53, n: 2, country: 'CANADA' }),
]));

// One player (id 7) in two pairs tied on rank and points.
write('Bwf_MensDoubles_TiedPairs.json', bwf([
  bwfRow(501, 505, 2200, { id: 7, n: 8, country: 'USA' }, { id: 8, n: 9, country: 'USA' }),
  bwfRow(502, 505, 2200, { id: 7, n: 8, country: 'USA' }, { id: 9, n: 10, country: 'USA' }),
]));

// ---- ESPN tennis ---------------------------------------------------------------------------------------------
const espnCountries = ['ITA', 'ESP', 'GER', 'SUI', 'USA', 'RUS', 'DNK', 'NOR', 'AUS', 'GRC', 'CAN', 'LUX'];
write('Espn_Atp_Singles.json', json({
  rankings: [{
    name: 'ATP Singles', update: '2026-09-10T06:00:00Z',
    ranks: espnCountries.map((c, i) => ({ current: i + 1, previous: i + 1, points: 11500 - i * 700, athlete: { citizenshipCountry: c, displayName: titleCase(i) } })),
  }],
}));

// ---- FIBA page: first table is the ranking; later tables are movers widgets; a date dropdown ------------------
const fibaTeams = [['usa', 'United States', 952.3], ['germany', 'Germany', 840.1], ['france', 'France', 811.4], ['serbia', 'Serbia', 780.0],
  ['canada', 'Canada', 760.9], ['australia', 'Australia', 731.2], ['lithuania', 'Lithuania', 700.5], ['spain', 'Spain', 690.0],
  ['slovenia', 'Slovenia', 640.3], ['new-zealand', 'New Zealand', 601.7], ['brazil', 'Brazil', 590.2], ['greece', 'Greece', 571.8]];
const fibaRow = ([slug, label, pts], i) =>
  `      <tr><td>${i + 1}.</td><td><a href="/fr/teams/${150 + i}-${slug}">${label}</a></td><td>${i + 1}.</td><td>${pts.toFixed(1)}</td><td>0</td></tr>`;
const fibaWidget = (title, picks) => `    <h2>${title}</h2>
    <table>
      <tbody>
${picks.map(([slug, label], i) => `      <tr><td>${i + 1}</td><td><a href="/fr/teams/${150 + i}-${slug}">${label}</a></td><td>+${3 - i}</td></tr>`).join('\n')}
      </tbody>
    </table>`;
write('Fiba_Ranking_Men.html', `<!DOCTYPE html>
<html lang="fr">
<head><meta charset="utf-8"><title>Sample ranking page</title></head>
<body>
  <main>
    <select name="rankingDatesselect">
      <option value="2026-09-01T00:00:00.000Z" selected="">1 Sep 2026</option>
      <option value="2026-07-13T00:00:00.000Z">13 Jul 2026</option>
      <option value="2026-03-03T00:00:00.000Z">3 Mar 2026</option>
    </select>
    <table>
      <thead><tr><th>#</th><th>Country</th><th>Zone rank</th><th>PTS</th><th>+/-</th></tr></thead>
      <tbody>
${fibaTeams.map(fibaRow).join('\n')}
      </tbody>
    </table>
${fibaWidget('Biggest movers', fibaTeams.slice(9, 12))}
${fibaWidget('Biggest drops', fibaTeams.slice(3, 6))}
  </main>
</body>
</html>`);

// ---- FIFA v3 API and the two pages with the ranking-schedule ids -------------------------------------------
const fifa = (rows) => json({ Results: rows.map(([code, rank, pts, label]) => ({ Rank: rank, IdCountry: code, Name: label, TotalPoints: pts })) });
write('Fifa_V3_Men.json', fifa([['ESP', 1, 1995.881879, 'Spain'], ['ARG', 2, 1880.2, 'Argentina'], ['FRA', 3, 1870.5, 'France'], ['ENG', 4, 1820.0, 'England'],
  ['BRA', 5, 1790.4, 'Brazil'], ['GER', 6, 1740.1, 'Germany'], ['NED', 7, 1730.9, 'Netherlands'], ['POR', 8, 1725.3, 'Portugal'],
  ['BEL', 9, 1700.0, 'Belgium'], ['ITA', 10, 1690.2, 'Italy'], ['MAR', 11, 1680.7, 'Morocco'], ['SMR', 12, 720.0, 'San Marino']]));
write('Fifa_V3_Women.json', fifa([['ESP', 1, 2066.8, 'Spain'], ['USA', 2, 2050.1, 'USA'], ['SWE', 3, 2010.3, 'Sweden'], ['GER', 4, 2000.0, 'Germany'],
  ['ENG', 5, 1990.6, 'England'], ['FRA', 6, 1960.2, 'France'], ['JPN', 7, 1950.9, 'Japan'], ['MRI', 8, 900.0, 'Mauritius'],
  ['SMR', null, null, 'San Marino'], ['AIA', null, null, 'Anguilla']]));

const fifaPage = (dates) => `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample FIFA ranking page</title></head>
<body>
  <div id="__next"></div>
  <script id="__NEXT_DATA__" type="application/json">${JSON.stringify({ props: { pageProps: { pageData: { ranking: { dates } } } } })}</script>
</body>
</html>`;
write('Fifa_WorldRanking_Men.html', fifaPage([
  { year: '2026', dates: [{ id: 'FRS_Male_Football_20260611', iso: '2026-07-20T08:37:28.979Z', dateText: '20 July 2026' }, { id: 'FRS_Male_Football_20260327', iso: '2026-04-03T10:00:00.000Z', dateText: '3 April 2026' }] },
  { year: '2025', dates: [{ id: 'id14870', iso: '2025-09-18T00:00:00.000Z', dateText: '18 September 2025' }] },
]));
write('Fifa_WorldRanking_Women.html', fifaPage([
  { year: '2026', dates: [{ id: 'FRS_Female_Football_20260419', iso: '2026-04-24T09:00:00.000Z', dateText: '24 April 2026' }, { id: 'FRS_Female_Football_20260110', iso: '2026-01-15T09:00:00.000Z', dateText: '15 January 2026' }] },
  { year: '2025', dates: [{ id: 'FRS_Female_Football_20251201', iso: '2025-12-05T09:00:00.000Z', dateText: '5 December 2025' }] },
]));

// ---- FIG pages: an h4 per series, a nav-tabs strip per series, a pane per apparatus -------------------------
const FIG_SERIES = [['wc', 'World Cup', 'Apparatus World Cup Ranking List 2026', 6], ['ch', 'World Challenge Cup', 'World Challenge Cup Ranking List 2026', 5]];
const POOL = ['USA', 'JPN', 'GBR', 'CHN', 'ITA', 'BRA', 'FRA', 'CAN', 'AUS', 'ESP', 'KOR', 'GER', 'UKR', 'TUR', 'HUN', 'ROU', 'BEL', 'NED', 'MEX', 'EGY'];
const figName = (i) => `${surname(i)}&nbsp;${given(i)}`;
// rows: [rank, ioc, athleteIndex | null (a national group), total]
const defaultRows = (count, offset) => Array.from({ length: count }, (_, i) => [i + 1, POOL[(offset + i) % POOL.length], offset + i, 60 - i * 4]);
const groupRows = (codes) => codes.map((c, i) => [i + 1, c, null, 60.5 - i * 3]);
const figRow = ([rank, ioc, athlete, total]) => athlete === null
  ? `            <tr><td data-label='Rank' class='bold' align='center'><b>${rank}</b></td><td data-label='NF'><nobr><img src='' alt='${ioc}'>&nbsp;${ioc}</nobr></td><td data-label='NF'>${ioc}</td><td data-label='Total' align='center'>${total}</td></tr>`
  : `            <tr><td data-label='Rank' class='bold' align='center'><b>${rank}</b></td><td data-label='NF'><nobr><img src='' alt='${ioc}'>&nbsp;${ioc}</nobr></td><td data-label='Name'>${figName(athlete)}</td><td data-label='Total' align='center'>${total}</td></tr>`;
const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-');
const figPage = (title, apparatus, special) => {
  let offset = 0;
  const sections = FIG_SERIES.map(([key, series, heading, count]) => {
    const tabs = apparatus.map((a) => `          <li><a data-toggle="tab" href="#${key}_${slug(a)}">${a}</a></li>`).join('\n');
    const panes = apparatus.map((a) => {
      const rows = special[`${series} / ${a}`] ?? defaultRows(count, (offset += 3));
      return `          <div id="${key}_${slug(a)}" class="tab-pane">
            <table class="table">
              <thead><tr><th>Rank</th><th>NF</th><th>Name</th><th>Total</th></tr></thead>
              <tbody>
${rows.map(figRow).join('\n')}
              </tbody>
            </table>
          </div>`;
    }).join('\n');
    return `      <h4>${heading}</h4>
      <div>
        <ul class="nav nav-tabs">
${tabs}
        </ul>
        <div class="tab-content">
${panes}
        </div>
      </div>`;
  }).join('\n');
  return `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>${title}</title></head>
<body>
  <div class="container">
${sections}
  </div>
</body>
</html>`;
};
write('Fig_Artistic_Men.html', figPage('Sample MAG ranking', ['Floor Exercise', 'Pommel Horse', 'Still Rings', 'Vault', 'Parallel Bars', 'Horizontal Bar'], {
  'World Cup / Floor Exercise': [[1, 'BLR', 0, 71], [2, 'PHI', 1, 66], [3, 'ISR', 2, 60], [4, 'JPN', 3, 55], [5, 'USA', 4, 50], [6, 'GBR', 5, 45]],
  'World Challenge Cup / Floor Exercise': [[1, 'GER', 6, 40], [2, 'TUR', 7, 36], [3, 'CRO', 8, 33], [4, 'ROU', 9, 30], [5, 'HUN', 10, 27]],
  'World Challenge Cup / Horizontal Bar': [[1, 'BUL', 11, 42], [1, 'GER', 12, 42], [3, 'JPN', 13, 38], [4, 'USA', 14, 35], [5, 'CHN', 15, 31]],
}));
write('Fig_Artistic_Women.html', figPage('Sample WAG ranking', ['Vault', 'Uneven Bars', 'Balance Beam', 'Floor Exercise'], {
  'World Cup / Balance Beam': [[1, 'ALG', 1, 70], [2, 'USA', 2, 65], [3, 'BRA', 3, 60], [4, 'CHN', 4, 55], [5, 'ITA', 5, 50], [6, 'CAN', 6, 45]],
}));
write('Fig_Rhythmic_Women.html', figPage('Sample RG ranking', ['Individual All-Around', 'Hoop', 'Ball', 'Clubs', 'Ribbon', 'Group All-Around', 'Group 5x', 'Group 3x+2x'], {
  'World Cup / Group 5x': groupRows(['CHN', 'RUS', 'ITA', 'ISR', 'BUL']),
  'World Cup / Group All-Around': groupRows(['ITA', 'CHN', 'ISR', 'BUL', 'ESP']),
  'World Cup / Group 3x+2x': groupRows(['ISR', 'ITA', 'CHN', 'ESP', 'BRA']),
  'World Challenge Cup / Group All-Around': groupRows(['ESP', 'BRA', 'UKR', 'MEX', 'EGY']),
  'World Challenge Cup / Group 5x': groupRows(['BRA', 'ESP', 'UKR', 'MEX', 'EGY']),
  'World Challenge Cup / Group 3x+2x': groupRows(['UKR', 'ESP', 'BRA', 'MEX', 'EGY']),
}));

// ---- FIH static JSON ---------------------------------------------------------------------------------------
const fihTeams = [['GER', 'Germany', 3720.41], ['NED', 'Netherlands', 3600.2], ['BEL', 'Belgium', 3500.7], ['AUS', 'Australia', 3400.0], ['IND', 'India', 3300.5], ['ENG', 'England', 3200.9],
  ['ARG', 'Argentina', 3100.1], ['ESP', 'Spain', 3000.3], ['RSA', 'South Africa', 2000.0], ['NZL', 'New Zealand', 1900.6], ['FRA', 'France', 1800.2], ['MAR', 'Morocco', 400.0]];
write('Fih_Outdoor_Men.json', json({ ranks: fihTeams.map(([code, label, pts], i) => ({ rank: i + 1, team_short_code: code, team_name: label, points: pts })) }));

// ---- ICC: the ranking is data["bat-rank"].rank; shortname is a team code ------------------------------------
const icc = (date, codes) => json({ data: { 'bat-rank': { rank_date: date, rank: codes.map((c, i) => ({ no: i + 1, shortname: c, rating: 126 - i * 5, points: 3000 - i * 100 })) } } });
write('Icc_Test_Men.json', icc('2026-09-12', ['AUS', 'SA', 'ENG', 'IND', 'NZ', 'SL', 'WI', 'PAK', 'BAN', 'ZIM']));
write('Icc_T20_Women.json', icc('2026-09-12', ['AUS-W', 'ENG-W', 'IND-W', 'NZ-W', 'SA-W', 'WI-W', 'SL-W', 'PAK-W', 'HK-W', 'SRL-W', 'ESW-W', 'SDA-W']));
write('Icc_T20_Men.json', icc('2026-09-12', ['IND', 'AUS', 'ENG', 'STH', 'GSY', 'IOM', 'JSY', 'SRL', 'ESW', 'SDA', 'NEP', 'UAE']));

// ---- SVNS: the standings page, the two series, their standings ------------------------------------------------
const MEN_SERIES = '11111111-2222-4333-8444-555555555555';
const WOMEN_SERIES = '66666666-7777-4888-9999-000000000000';
write('Svns_Standings.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample standings page</title></head>
<body>
  <section class="series-standings" data-widget="svns/series-standings" data-series-ids="${MEN_SERIES},${WOMEN_SERIES}" data-flag-type="squares"></section>
</body>
</html>`);
write('Svns_Series_Men.json', json({ id: MEN_SERIES, name: 'Sample men\'s series', sport: 'mrs' }));
write('Svns_Series_Women.json', json({ id: WOMEN_SERIES, name: 'Sample women\'s series', sport: 'wrs' }));
const svns = (codes) => json({ entries: codes.map((c, i) => ({ position: i + 1, team: { abbreviation: c, countryCode: null }, totalPoints: 52 - i * 4 })) });
write('Svns_Standings_Men.json', svns(['RSA', 'ARG', 'FIJ', 'NZL', 'FRA', 'ESP', 'AUS', 'GBR', 'IRL', 'USA', 'KEN', 'URU']));
write('Svns_Standings_Women.json', svns(['AUS', 'NZL', 'FRA', 'USA', 'CAN', 'GBR', 'IRL', 'JPN', 'BRA', 'FIJ', 'ESP', 'RSA']));

// ---- Volleyball World: indoor teams with decimalPoints; beach pairs with player names and integer points ------
const indoor = [['POL', 'Poland', 400.16], ['ITA', 'Italy', 390.4], ['FRA', 'France', 380.2], ['JPN', 'Japan', 370.0], ['BRA', 'Brazil', 360.7], ['USA', 'USA', 350.1],
  ['SLO', 'Slovenia', 340.3], ['GER', 'Germany', 330.9], ['ARG', 'Argentina', 320.5], ['CUB', 'Cuba', 310.0], ['SRB', 'Serbia', 300.2], ['NZL', 'New Zealand', 40.0]];
write('VolleyballWorld_Men.json', json({ teams: indoor.map(([code, label, pts], i) => ({ rankToDisplay: i + 1, federationCode: code, name: label, decimalPoints: pts, points: Math.round(pts) })) }));
const beach = ['SWE', 'BRA', 'NOR', 'USA', 'BRA', 'GER', 'NED', 'QAT', 'ITA', 'POL', 'CZE', 'AUS'];
write('VolleyballWorld_Beach_Men.json', json({ teams: beach.map((code, i) => ({
  rankToDisplay: i + 1, federationCode: code,
  name: `${surname(2 * i)[0]}${surname(2 * i).slice(1).toLowerCase()}/${surname(2 * i + 1)[0]}${surname(2 * i + 1).slice(1).toLowerCase()}, ${given(2 * i + 1)[0]}`,
  player1Name: titleCase(2 * i), player2Name: titleCase(2 * i + 1), points: 8020 - i * 400,
})) }));

// ---- WBSC: the rankings API rows and the page with the release dates -----------------------------------------
const wbscTeams = [['JPN', 1, 6337], ['USA', 2, 6000], ['KOR', 3, 5500], ['TPE', 4, 5300], ['VEN', 5, 4800], ['MEX', 6, 4500], ['DOM', 7, 4200], ['CUB', 8, 4000],
  ['NED', 9, 3800], ['GER', 10, 2000], ['PAN', 11, 1500], ['TUR', 11, 1500]];   // the bottom two tie
write('Wbsc_Baseball_Men.json', json({ rankings: wbscTeams.map(([ioc, position, points]) => ({ position, ioc, date: '2026-03-26', points })) }));
const releaseDates = [['2026-03-26', 'baseball-m'], ['2025-12-11', 'baseball-m'], ['2026-09-09', 'baseball-w'], ['2026-05-14', 'softball-m'], ['2025-11-20', 'softball-m'],
  ['2025-12-31', 'softball-w'], ['2026-08-07', 'baseball5-coed'], ['2026-02-01', 'baseball5-coed']]
  .map(([date, sport]) => ({ date, sport, year: Number(date.slice(0, 4)), formatted: `${date.slice(8)}/${date.slice(5, 7)}/${date.slice(0, 4)}` }));
write('Wbsc_Rankings.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample rankings page</title></head>
<body>
  <div id="app"></div>
  <script>window.releaseDates = ${JSON.stringify(releaseDates)};</script>
</body>
</html>`);

// ---- Wikipedia IIHF article (Parsoid HTML): a section per gender, a sortable table each --------------------------
const iihfRow = (rank, prev, team, gender, total) => {
  const link = `<a rel="mw:WikiLink" href="./${team.replace(/ /g, '_')}_${gender}'s_national_ice_hockey_team">${team}</a>`;
  return `        <tr><td>${rank}</td><td>${prev}</td><td style="text-align:left">${link}</td><td>${total === null ? '—' : total - 100}</td><td>${total === null ? '—' : `<b>${total}</b>`}</td></tr>`;
};
const iihfTable = (id, title, gender, ranked, unranked, marker) => `    <section>
      <h2 id="${id}">${title}</h2>
      <table class="wikitable sortable" style="text-align:center">
        <tbody>
        <tr><th>Rank</th><th>Previous</th><th>Team</th><th>Previous total</th><th>Current total</th></tr>
${ranked.map(([team, total], i) => iihfRow(i + 1, i + 1, team, gender, total)).join('\n')}
${unranked.map((team) => iihfRow(marker, marker, team, gender, null)).join('\n')}
        </tbody>
      </table>
    </section>`;
write('Wikipedia_IihfWorldRanking.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample IIHF World Ranking article</title></head>
<body>
  <section><h2 id="Overview">Overview</h2><p>A sample of the article's two ranking tables.</p></section>
${iihfTable("Men's_rankings", "Men's rankings", 'men',
    [['Switzerland', 5335], ['Canada', 5305], ['United States', 5280], ['Sweden', 5250], ['Finland', 5200], ['Czech Republic', 5150], ['Germany', 4900], ['Slovakia', 4700],
     ['Latvia', 4500], ['Denmark', 4300], ['Austria', 4100], ['Armenia', 805]],
    ['Russia', 'India', 'Morocco'], 'NR')}
${iihfTable("Women's_rankings", "Women's rankings", 'women',
    [['United States', 5460], ['Canada', 5400], ['Czech Republic', 5125], ['Finland', 5100], ['Sweden', 5000], ['Switzerland', 4900], ['Japan', 4800], ['Germany', 4600],
     ['Hungary', 4300], ['Singapore', 1135]],
    ['Philippines'], 'new')}
</body>
</html>`);

// ---- World Rugby union rankings ------------------------------------------------------------------------------
const rugby = ['RSA', 'IRL', 'NZL', 'FRA', 'ENG', 'ARG', 'SCO', 'AUS', 'FIJ', 'ITA', 'GEO', 'ASA'];
write('WorldRugby_Union_Men.json', json({
  label: 'Sample men\'s rankings', effective: { millis: 1789344000000, label: '2026-09-14' },
  entries: rugby.map((c, i) => ({ pos: i + 1, pts: i === 0 ? 95.09 : 90 - i * 3.5, team: { countryCode: c, name: c } })),
}));

// ---- WTA players API: a top-level array, one page --------------------------------------------------------------
const wta = ['CZE', 'USA', 'ITA', 'CAN', 'AUS', 'BEL', 'LAT', 'JPN', 'CHN', 'UKR', 'ESP', 'ROU'];
write('Wta_Doubles.json', json(wta.map((c, i) => ({
  player: { id: 300 + i, countryCode: c, fullName: titleCase(i) }, ranking: i + 1, points: 11460 - i * 500, rankedAt: '2026-09-14T00:00:00Z',
}))));
```

Run: `node tests/SportsRankingService.Tests/Samples/generate.mjs && ls tests/SportsRankingService.Tests/Samples | wc -l`
Expected: `31` (29 samples, the script, and `SAMPLES.md` from the next step counts once it exists; before it, 30).

- [ ] **Step 5: Write SAMPLES.md**

`tests/SportsRankingService.Tests/Samples/SAMPLES.md`:

```markdown
# Samples

Every file here is synthetic: written by `generate.mjs`, one small response per feed in exactly the shape its
parser or resolver reads. Country codes and names are facts; athlete names are invented (the NATO alphabet as
surnames); points, ids and dates are made up or copied from the tests that pin them. Nothing here is copied
from a federation's site, so the repository distributes none of their content or embedded keys.

To change a sample, edit `generate.mjs` and run `node tests/SportsRankingService.Tests/Samples/generate.mjs`;
commit the script and the regenerated files together with the test expectations they change.

## The optional capture pass

Real responses prove that a parser still reads the live format. Save one per feed under
`tests/SportsRankingService.Tests/Captures/` (git-ignored) using the sample's file name, and `CaptureTests`
runs every parser over them; without the folder those tests are skipped. Capture with `curl` and the scraper's
own User-Agent, from the URLs in `src/SportsRankingService/serviceconfig.json` (the FIFA, WBSC and SVNS
resolvers read the pages named in `Services/UrlResolvers/`). Never commit a capture: a federation's page is
its copyright and carries its browser keys.

## Shape notes per feed

| Sample | Shape the parser reads |
|---|---|
| Bwf_*.json | Laravel envelope `results.data[]`: `rank`, `points` as a string, `player1_id`/`player2_id`, `player1_model.name_display_bold` as HTML, `p1_country_model.name` as a country name (no code); pairs on doubles rows; "Athlete Independent Neutral" is dropped, "<COUNTRY> Independent" counts for the country |
| Bwf_MensSingles_RepeatedRows.json | five rows tied at rank 925, two players listed twice (a new row id, the same `player1_id`) |
| Bwf_MensDoubles_TiedPairs.json | one player in two pairs tied on rank and points: listed once |
| Espn_Atp_Singles.json | `rankings[0].ranks[]`: `current`, `athlete.citizenshipCountry`, `athlete.displayName`, `points`; `rankings[0].update` is the date |
| Fiba_Ranking_Men.html | the first `table`'s `tbody tr`: `td[1]` "1.", a link `/xx/teams/<id>-<slug>` whose slug is the country, `td[4]` PTS; later tables are movers widgets and are ignored; `select[name=rankingDatesselect]` with the selected option's `value` as the date |
| Fifa_V3_*.json | `Results[]`: `Rank` (null for an unranked team), `IdCountry` (FIFA codes, home nations kept), `TotalPoints` |
| Fifa_WorldRanking_*.html | `script#__NEXT_DATA__` with `props.pageProps.pageData.ranking.dates[].dates[].{id,iso}`; newest by `iso` |
| Fig_*.html | an `h4` per series, then the first `ul.nav-tabs` after it with one `a[href=#pane]` per apparatus; in the pane, `tr` with `td[data-label=Rank]`, `td[data-label=NF] img[alt=IOC]`, `td[data-label=Name]` (absent for rhythmic groups), `td[data-label=Total]` |
| Fih_Outdoor_Men.json | `ranks[]`: `rank`, `team_short_code` (IOC), `points` |
| Icc_*.json | `data["bat-rank"].rank[]`: `no`, `shortname` (team code, `-W` suffix for women, two-letter codes, `SRL` is Sierra Leone), `rating`; `rank_date` |
| Svns_Standings.html | `[data-series-ids="<men guid>,<women guid>"]` |
| Svns_Series_*.json | `sport`: `mrs` or `wrs` |
| Svns_Standings_*.json | `entries[]`: `position`, `team.abbreviation` (IOC), `totalPoints` |
| VolleyballWorld_Men.json | `teams[]`: `rankToDisplay`, `federationCode`, `decimalPoints` (else `points`) |
| VolleyballWorld_Beach_Men.json | as above with `player1Name` present and `name` the pair; integer `points` |
| Wbsc_Baseball_Men.json | `rankings[]`: `position`, `ioc`, `date`, `points`; positions can tie |
| Wbsc_Rankings.html | `{"date":"yyyy-MM-dd","sport":"<sportId>"...}` objects anywhere in the page; newest per sport |
| Wikipedia_IihfWorldRanking.html | a `section` whose `h2` id is `Men's_rankings` or `Women's_rankings`, its first `table.sortable`, rows `td`: rank (`NR`/`new` skipped), previous, a link containing `national_ice_hockey_team`, ..., total in `td/b` |
| WorldRugby_Union_Men.json | `entries[]`: `pos`, `team.countryCode` (IOC), `pts`; `effective.label` is the date |
| Wta_Doubles.json | a top-level array: `ranking`, `player.countryCode`, `player.fullName`, `points`, `rankedAt`; `countryCode` null or `NCD` is dropped |
```

- [ ] **Step 6: Run the theory to see it pass**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~SampleCountryCodesTests"`
Expected: 55 passing cases (19 listed feeds + 36 FIG tables). Other test classes still fail on their old expectations; that is the next two tasks.

- [ ] **Step 7: Commit**

```bash
git add -A tests/SportsRankingService.Tests .gitignore
git commit -m "Replace the captured fixtures with synthetic samples (loader and codes theory)"
```

---

### Task 2: Re-pin the JSON parser tests to the samples

**Files:**
- Modify: `tests/SportsRankingService.Tests/Parsers/BwfParserTests.cs`, `EspnTennisParserTests.cs`, `FifaV3ParserTests.cs`, `FihParserTests.cs`, `IccParserTests.cs`, `SvnsParserTests.cs`, `VolleyballWorldParserTests.cs`, `WbscParserTests.cs`, `WorldRugbyParserTests.cs`, `WtaParserTests.cs`

**Interfaces:**
- Consumes: `Sample.Read` and the sample names from Task 1.

- [ ] **Step 1: Run the ten classes to see which assertions fail**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~Parsers"`
Expected: failures on counts and names (100 vs 12, "Jonatan CHRISTIE" vs "Jonah ALPHA", ...). Every failure below is an expectation about the old capture.

- [ ] **Step 2: Change the expectations, keeping each test's intent**

Only the listed lines change; the rest of each file stays.

`BwfParserTests.cs`:
- `Singles_yield_one_entry_per_player`: `Assert.Equal(12, rows.Count);` `IDN` at 1, `AZE` at 12.
- `Doubles_yield_one_entry_per_partner_with_the_pairs_position`: position 1 → `["KOR","KOR"]`; the mixed pair is position **5** → `["KOR","MYS"]`.
- `Neutral_athletes_are_dropped`: the doubles sample has ten pairs, one of them neutral: `Assert.Equal(18, rows.Count);` (replace the `InRange`).
- `A_player_the_feed_lists_twice_is_one_entry`: unchanged (`["PER","DOM","CAN"]` at 925).
- `A_player_in_two_tied_pairs_is_listed_once_at_that_rank`: competitors `["Sam INDIA", "Noa JULIET", "Eli KILO"]`; the `RankEntry(505, "USA", r.Competitor, 2200m)` line is unchanged.
- `Singles_carry_points_the_country_name_and_the_player_as_competitor`: `87631m`, `IDN`, `"Jonah ALPHA"`.
- `Doubles_partners_each_carry_their_own_name`: `["Jonah ALPHA", "Mika BRAVO"]`, `KOR`, `114099m`.
- Doc comments that cite "2026-09-18 counts" (172 echoes, rows 938-942, `player1_id` 88711) become: "The sample is five rows tied at rank 925: three players, two of them listed twice." and "The sample is one player's two pairs tied at rank 505."

`EspnTennisParserTests.cs`: count `12`, `ITA` at 1, `LUX` at 12; date `2026-09-10` unchanged; points `11500m`, competitor `"Jonah Alpha"`.

`FifaV3ParserTests.cs`: file names `Fifa_V3_Men.json` / `Fifa_V3_Women.json`; men count `12`, `ESP` at 1, `SMR` at 12; women: comment "The sample lists 10 teams; the last 2 have a null rank.", count `8`, `ESP` at 1, `MUS` at 8; codes test unchanged; points `1995.881879m` unchanged.

`FihParserTests.cs`: count `12`, `DEU` at 1, `MAR` at 12; points `3720.41m` (the sample keeps a short decimal so the generator round-trips it).

`IccParserTests.cs`: Test men unchanged (10 rows, AUS/ZWE, codes, WI, date, rating 126); women count `12` (was 80), the rest unchanged.

`SvnsParserTests.cs`: unchanged (12 rows, RSA/URU, AUS/RSA, 52 points).

`VolleyballWorldParserTests.cs`: indoor count `12`, `POL` at 1, `NZL` at 12, `400.16m`; beach count `12`, `SWE`, competitor `"Alpha/Bravo, M"`, `8020m`, the BRA-twice assertion unchanged.

`WbscParserTests.cs`: count `12`, `JPN` at 1, `TUR` last with the comment "positions tie at the bottom, so 11 is shared"; codes, date `2026-03-26`, points `6337m` unchanged.

`WorldRugbyParserTests.cs`: count `12`, `ZAF` at 1, `ASM` at 12; date unchanged; points `95.09m`.

`WtaParserTests.cs`: count `12`, `CZE` at 1, `ROU` at 12; date unchanged; points `11460m`, competitor `"Jonah Alpha"`.

- [ ] **Step 3: Run the ten classes to see them pass**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~Parsers"`
Expected: the JSON parser classes pass; `FibaParserTests`, `FigParserTests` and `WikipediaIihfParserTests` still fail (Task 3).

- [ ] **Step 4: Commit**

```bash
git add tests/SportsRankingService.Tests/Parsers
git commit -m "Pin the JSON parser tests to the synthetic samples"
```

---

### Task 3: Re-pin the HTML parser, resolver and runner tests

**Files:**
- Modify: `tests/SportsRankingService.Tests/Parsers/FibaParserTests.cs`, `FigParserTests.cs`, `WikipediaIihfParserTests.cs`
- Modify: `tests/SportsRankingService.Tests/Services/FifaDateIdTests.cs`, `SvnsSeriesResolverTests.cs`, `WbscReleaseDateResolverTests.cs`, `RankingSourceRunnerTests.cs`

- [ ] **Step 1: Run them to see the failures**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~FibaParserTests|FullyQualifiedName~FigParserTests|FullyQualifiedName~WikipediaIihfParserTests|FullyQualifiedName~Services"`
Expected: count and name failures; the resolver tests already pass (the samples keep their ids and dates).

- [ ] **Step 2: Change the expectations**

`FibaParserTests.cs`:
- `Every_row_has_a_three_letter_code_and_a_positive_position`: `Assert.Equal(12, rows.Count);` replaces the `>= 150` line.
- `Ignores_the_movers_and_drops_widget_tables`: comment "The page also carries movers and drops tables whose rows repeat ranked countries."; both `159` become `12`.
- The rest (USA/DEU/FRA at 1-3, date `2026-9-1`, `952.3m`) is unchanged.

`FigParserTests.cs`:
- `Selects_the_apparatus_table_of_the_requested_series`: `worldCup.Count == 6`, `BLR`/`PHL`/`ISR` unchanged, `challengeCup.Count == 5`, `DEU` at 1 unchanged.
- `Carries_the_athlete_as_competitor_with_plain_spaces_and_no_country_name`: `"ALPHA Jonah"`.
- `Tied_athletes_keep_the_same_position`: unchanged (`["BGR","DEU"]`, no position 2).
- `Womens_artistic_page_has_its_own_apparatus_set`: count `6`, `DZA`, `"BRAVO Mika"`.
- `Rhythmic_group_events_are_national_teams_without_a_competitor`: count `5`; `CHN` at 1 with null competitor; `RUS` at 2 unchanged.
- `Every_table_yields_three_letter_codes_and_positive_positions`: `rows.Count >= 5` with the message "expected a table, got {rows.Count} rows"; replace `EveryTable()` and its page list with `[MemberData(nameof(Feeds.All), MemberType = typeof(Feeds))]`? No: that theory takes a parser too. Keep `EveryTable()` but build it from `Feeds.FigPages` and `Feeds.FigSeries` so the page list has one home:

```csharp
    public static TheoryData<string, string> EveryTable()
    {
        TheoryData<string, string> data = new();
        foreach ((string page, string[] apparatus) in Feeds.FigPages)
        {
            foreach (string series in Feeds.FigSeries)
            {
                foreach (string a in apparatus)
                {
                    data.Add(page, $"{series} / {a}");
                }
            }
        }

        return data;
    }
```
- `Carries_the_total_as_points`: `71m` unchanged.

`WikipediaIihfParserTests.cs`:
- Men: count `12`; `(1, "CHE", 5335m)`, `(2, "CAN", 5305m)` unchanged; the last is `new RankEntry(12, "ARM", Points: 805m)` at position 12.
- Women: count `10`; `(1, "USA", 5460m)`, `(3, "CZE", 5125m)` unchanged; the last is `new RankEntry(10, "SGP", Points: 1135m)` at position 10.
- `Teams_listed_as_not_ranked_are_not_entries`: unchanged (RUS/IND/MAR men, PHL women).

`FifaDateIdTests.cs`: unchanged apart from the loader rename (ids and `2026-07-20` are in the sample).

`SvnsSeriesResolverTests.cs`: ids `["11111111-2222-4333-8444-555555555555", "66666666-7777-4888-9999-000000000000"]`.

`WbscReleaseDateResolverTests.cs`: unchanged (the sample carries the same release dates).

`RankingSourceRunnerTests.cs`:
- `A_paged_feed_fetches_pages_in_order_until_an_empty_one_and_concatenates_their_entries`: page 1 is `WtaPage("2026-09-14T00:00:00Z", (13, "USA", "Ann"), (14, "FRA", "Bea"))`; `Assert.Equal(14, snapshot.Entries.Count)`; `"FRA"` at position `14`.
- `A_page_with_a_different_ranking_date_fails_the_parse_because_the_list_changed_between_pages`: page 1 entry `(13, "USA", "Ann")`.
- `A_page_that_cannot_be_fetched_fails_the_whole_feed`: page 2 entry `(25, "USA", "Cat")` (any position; keep it plausible).
- `Static_feed_is_fetched_parsed_and_stamped`: `12` entries.
- `Resolver_runs_its_preliminary_request_and_supplies_the_ranking_date`: fixture name `Fifa_V3_Men.json`; `12` entries.
- `Selector_reaches_the_parser_for_a_page_holding_several_tables` and `Feeds_sharing_a_page_fetch_it_once_and_each_parse_their_own_table`: `21` becomes `5`.

- [ ] **Step 3: Run the whole scraper test project**

Run: `dotnet test tests/SportsRankingService.Tests`
Expected: all pass (the curl tests need `curl` on the PATH, as before), one skip (`MissingSourcesTests`).

- [ ] **Step 4: Commit**

```bash
git add tests/SportsRankingService.Tests
git commit -m "Pin the HTML parser, resolver and runner tests to the synthetic samples"
```

---

### Task 4: The capture pass, and the docs

**Files:**
- Create: `tests/SportsRankingService.Tests/CaptureTests.cs`
- Modify: `src/SportsRankingService/CLAUDE.md:25,56`
- Modify: `CLAUDE.md:25,76` (the root file: the test command comment and the Tests paragraph)

- [ ] **Step 1: Write the capture theory**

`tests/SportsRankingService.Tests/CaptureTests.cs`:

```csharp
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Tests;

/// <summary>
/// The optional pass over real responses saved under Captures/ (git-ignored; see Samples/SAMPLES.md): every
/// parser still reads the live format, a real list is long, and every code it yields has a name. Skipped when
/// the folder is absent. A capture that is missing while others exist fails its case with the file name.
/// </summary>
public class CaptureTests
{
    [CaptureTheory]
    [MemberData(nameof(Feeds.All), MemberType = typeof(Feeds))]
    public void A_real_response_parses_to_a_full_list_of_named_countries(IRankingParser parser, string capture, string? selector)
    {
        var rows = parser.Parse(Capture.Read(capture), selector).Entries;

        Assert.True(rows.Count >= 10, $"{capture} {selector}: expected a real list, got {rows.Count} rows");
        Assert.All(rows, r => Assert.Matches("^[A-Z]{2,3}$", r.ISO3));

        IEnumerable<string> unnamed = rows.Select(r => r.ISO3).Distinct().Where(code => !CountryUtil.TryGetCountryName(code, out _)).Order();
        Assert.True(!unnamed.Any(), $"{capture}: no country name for {string.Join(", ", unnamed)}");
    }
}
```

- [ ] **Step 2: Run it twice: with the captures present and absent**

Run: `dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~CaptureTests"`
Expected (the real captures are in `Captures/` from Task 1 Step 1): 55 cases pass, except the two FIFA lists which the old names no longer match: rename the two captures locally first: `mv Captures/Fifa_V3_Men_FRS_20260611.json Captures/Fifa_V3_Men.json; mv Captures/Fifa_V3_Women_FRS_20260419.json Captures/Fifa_V3_Women.json; rm Captures/Fifa_V3_Women.json.bak 2>/dev/null` (the 139-byte error response named `Fifa_V3_Women.json` in the old folder is deleted, not kept). Then all 55 pass.

Then: `mv tests/SportsRankingService.Tests/Captures /tmp/captures-aside; rm -rf tests/SportsRankingService.Tests/bin/Debug/net11.0/Captures; dotnet test tests/SportsRankingService.Tests --filter "FullyQualifiedName~CaptureTests"; mv /tmp/captures-aside tests/SportsRankingService.Tests/Captures`
Expected: 55 skipped, 0 failed. (Use the scratchpad directory instead of `/tmp` on Windows.)

- [ ] **Step 3: Update the docs**

`src/SportsRankingService/CLAUDE.md` line 25 becomes:

> **Tests are sample-based.** `tests/SportsRankingService.Tests/Samples/` holds one synthetic response per feed, written by `Samples/generate.mjs` in exactly the shape its parser reads (`SAMPLES.md` lists the shapes); nothing in the repository is copied from a federation's site. Parser tests are pure: `new XParser().Parse(Sample.Read("..."))`, then assert count and known positions. `Feeds.All` is the one list of every feed shape; `SampleCountryCodesTests` runs it over the samples and `CaptureTests` over real responses saved under the git-ignored `Captures/` folder (same file names; skipped when the folder is absent), which is how a live format change is caught before a run. `RankingSourceRunnerTests` covers the whole pipeline for one item with a fake `IHttpFetcher` keyed by URL, including resolver round-trips. A skipped test in `MissingSourcesTests` names a feed whose source is dead or blocks scripted clients. Never make a test pass by weakening its assertions. `InternalsVisibleTo` exposes internals to the test and benchmark projects.

Line 56 ("Adding a feed") becomes:

> **Adding a feed.** Capture a real response into the git-ignored `tests/SportsRankingService.Tests/Captures/` to learn its shape, add a synthetic sample of that shape to `Samples/generate.mjs` (regenerate, list it in `SAMPLES.md` and in `Feeds.All`); write the parser test against the sample (red); add a `<Feed>Parser` subclassing one of the two base classes with a unique `SourceName`; register it in `AddRankingPipeline`; add the item(s) to `serviceconfig.json`. If the URL needs a discovered id or date, add an `IUrlResolver` the same way. If one response holds several rankings, give each item a `Selector` and have the parser interpret it. Never commit a capture.

Root `CLAUDE.md` line 25 comment: `# the scraper alone: synthetic samples and SQLite, no Docker; real captures under the ignored Captures/ folder run too when present`. In the Tests paragraph (line 76) replace "The promotion guard's tests" sentence's preceding text about the scraper? It does not mention fixtures; add after "`ViewContractTests` pins the view's columns.": "The scraper's parser tests run on synthetic samples (`tests/SportsRankingService.Tests/Samples/`, generated by `generate.mjs`), never on copied federation pages; real captures are an optional local pass."

- [ ] **Step 4: Run everything the scraper has, then commit**

Run: `dotnet test tests/SportsRankingService.Tests`
Expected: pass; `grep -rn "Fixture" tests/SportsRankingService.Tests src tools --include=*.cs --include=*.md --include=*.csproj` prints nothing.

```bash
git add -A tests/SportsRankingService.Tests src/SportsRankingService/CLAUDE.md CLAUDE.md
git commit -m "Add the optional capture pass and document the samples"
```

---

### Task 5: Pull request, CI, merge

- [ ] **Step 1: Push the branch and open the PR**

```bash
git push -u origin synthetic-samples
gh pr create --title "Replace captured fixtures with synthetic samples" --body-file - <<'EOF'
The test project no longer carries copied federation pages (with their copyright notices and browser keys). Every parser test runs on a synthetic sample written by `Samples/generate.mjs`; real captures stay local under the git-ignored `Captures/` folder and run through `CaptureTests` when present. The history rewrite that removes the old captures follows this merge.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
```

- [ ] **Step 2: Wait for CI, then merge**

Run: `gh pr checks --watch` then `gh pr merge --merge --delete-branch`
Expected: the six CI jobs green; `main` fast-forwards by a merge commit; `git pull` locally.

---

### Task 6: Rewrite the history and push it

**Preconditions:** `main` is merged and pulled, `git status` clean, no other worktree (`git worktree prune`), `gitleaks` on the PATH (`command -v gitleaks`).

- [ ] **Step 1: Save every ref outside the repository**

```bash
git worktree prune
git bundle create ../WorldRankGuesser-pre-rewrite-2026-09-23.bundle --all
git bundle verify ../WorldRankGuesser-pre-rewrite-2026-09-23.bundle
```
Expected: "The bundle records a complete history" and the ref list (main, three local branches, the tag, origin/main).

- [ ] **Step 2: Rewrite**

```bash
K=$(git show 04221ddd:WorldRankGuesser/wwwroot/urls.json | grep -oE 'api_key=[A-Za-z0-9_-]+' | head -1 | cut -d= -f2)
test ${#K} -eq 32 || { echo "key not found"; exit 1; }
TMP="$SCRATCHPAD/filter-branch"    # a directory outside the repository, on the same drive
FILTER_BRANCH_SQUELCH_WARNING=1 git filter-branch -f -d "$TMP" \
  --index-filter 'git rm -r -q --cached --ignore-unmatch tests/SportsRankingService.Tests/Fixtures SportsRankingService.Tests/Fixtures WorldRankGuesser/wwwroot/remotehtml' \
  --tree-filter "for f in SportsRankingService/serviceconfig.json WorldRankGuesser/wwwroot/urls.json; do [ -f \"\$f\" ] && sed -i \"s/$K/REDACTED_SPORTRADAR_KEY/g\" \"\$f\"; done; true" \
  --tag-name-filter cat --prune-empty -- --all
```
Expected: "Rewrite <sha> (143/143)" then "Ref 'refs/heads/main' was rewritten" for main, the three local branches, `refs/remotes/origin/main` and the tag.

- [ ] **Step 3: Verify nothing reachable carries the content**

```bash
git for-each-ref --format='%(refname)' refs/original/ | xargs -r -n1 git update-ref -d
git reflog expire --expire=now --all && git gc --prune=now -q
echo "sportradar: $(git log --all --oneline -S"$K" | wc -l)"
echo "fixture paths: $(git log --all --format= --name-only | grep -cE '(^|/)Fixtures/|remotehtml/')"
echo "google keys: $(git grep -l -E 'AIza[0-9A-Za-z_-]{30,}' $(git rev-list --all) | wc -l)"
echo "azure key: $(git grep -l 'NEXT_CLIENT_COGNITIVE_SEARCH_KEY' $(git rev-list --all) | wc -l)"
gitleaks git --log-opts="--all" --redact --report-format json --report-path artifacts/gitleaks-after.json . ; echo "gitleaks exit $?"
dotnet test tests/SportsRankingService.Tests   # the rewritten tip still builds and passes
```
Expected: the four counts are `0`; gitleaks exits 0 ("no leaks found"); tests pass.

- [ ] **Step 4: Push main and the tag, with the ruleset paused**

```bash
gh api --method PUT repos/joseph-leo/WorldRankGuesser/rulesets/23907343 -f enforcement=disabled --jq .enforcement
git push --force origin main
git push --force origin refs/tags/scraper-net8-baseline
gh api --method PUT repos/joseph-leo/WorldRankGuesser/rulesets/23907343 -f enforcement=active --jq .enforcement
git ls-remote origin
```
Expected: `disabled`, two forced updates, `active`; `ls-remote` shows `main` at the new tip and the tag.

- [ ] **Step 5: Close the two alerts and write the support request**

```bash
for n in 1 2; do
  gh api --method PATCH repos/joseph-leo/WorldRankGuesser/secret-scanning/alerts/$n \
    -f state=resolved -f resolution=used_in_tests \
    -f resolution_comment="A third party's browser-side key inside a saved federation page used as a test fixture; the fixtures were replaced by synthetic samples and removed from the whole history on 2026-09-23." --jq .state
done
```
Expected: `resolved` twice.

The owner sends this to GitHub Support (https://support.github.com, "Remove sensitive data / cached views"), because only they can garbage-collect the unreachable commits on the server:

> Repository: joseph-leo/WorldRankGuesser (public). On 2026-09-23 I rewrote the history to remove test fixtures that were copies of third-party web pages containing their client-side API keys (GitHub secret-scanning alerts 1 and 2, now resolved), and a 2023 trial API key of my own. The old commits are no longer reachable from any branch or tag. Please run garbage collection and clear cached views so the old commits (for example 5d4dae9b374dcba0238845901d4a229dc20cc9bf, 4c3c3d5f, 63aa2fafdd3e614ef60fee0be6690bd7e0e926ee, 04221dddeb856cdf03823ae17f32423d2e31ee33, e2334a1f27a62aa5ecf8c2bed94f06fcbaeff327) stop resolving. There are no forks.

- [ ] **Step 6: Record it**

Append to `docs/superpowers/plans/2026-09-22-phase-2c-azure-pipelines-going-public.md` after Task 16 a dated note: "2026-09-23: the history scan found the fixtures' third-party browser keys; fixtures replaced by synthetic samples and the history rewritten (see `2026-09-23-synthetic-samples-and-history-rewrite.md`); the support GC request is the owner's." Commit it through a normal PR (the ruleset is active again).
