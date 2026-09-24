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
