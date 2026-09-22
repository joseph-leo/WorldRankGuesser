# Fixtures

One real response per feed, captured with a browser User-Agent on 2026-09-15.
Tests assert against these files, not live sites. Re-capture a file only when a
federation changes its format, and update the affected test's expectations in
the same commit.

| File | Source URL | Status on capture |
|---|---|---|
| Fih_Outdoor_Men.json | https://www.fih.hockey/datafeeds/static/json/en/OutdoorRanking/outdoorranking_m.json | parses |
| WorldRugby_Union_Men.json | https://api.wr-rims-prod.pulselive.com/rugby/v3/rankings/mru?language=en | parses |
| Espn_Atp_Singles.json | https://site.web.api.espn.com/apis/site/v2/sports/tennis/atp/rankings?region=us&lang=en | parses |
| Wta_Doubles.json | https://api.wtatennis.com/tennis/players/ranked?page=0&pageSize=100&type=rankDoubles&sort=asc&name=&metric=DOUBLES | parses |
| VolleyballWorld_Men.json | https://en.volleyballworld.com/api/v1/worldranking/volleyball/1/0/500 | parses; recaptured 2026-09-17 at the API's 500-row maximum (148 teams, the full list) |
| VolleyballWorld_Beach_Men.json | https://en.volleyballworld.com/api/v1/worldranking/beachvolleyball/0/0/500 | recaptured 2026-09-17; a pair per row (`player1Name`, `player2Name`, `name`), integer `points`; 500 rows is the API's per-request maximum and the beach list is deeper (multi-page spec) |
| Icc_T20_Women.json | https://assets-icc.sportz.io/cricket/v1/ranking?...&comp_type=t20w&type=team | parses; `shortname` carries a `-W` suffix |
| Icc_Test_Men.json | https://assets-icc.sportz.io/cricket/v1/ranking?...&comp_type=test&type=team | parses; two-letter codes SA, NZ, SL, WI |
| Icc_T20_Men.json | https://assets-icc.sportz.io/cricket/v1/ranking?...&comp_type=t20&type=team | captured 2026-09-17; the deepest ICC list (102 teams) and the source of the ICC-only codes STH, GSY, IOM, JSY, SRL, ESW, SDA |
| Fifa_Overview_Men_id14870.json | https://inside.fifa.com/api/ranking-overview?locale=en&dateId=id14870 | parses; the API returns an empty list for the newer `FRS_*` date ids |
| Fifa_WorldRanking_Men.html | https://inside.fifa.com/fifa-rankings/world-ranking/men | page data still embeds the ranking dates |
| Fiba_Ranking_Men.html | https://www.fiba.basketball/rankingmen (redirects to /fr/ranking/men) | markup changed: no IOC column, country only in the team link slug |
| Wbsc_Rankings.html | https://rankings.wbsc.org/ | markup changed: the ranking table is empty in the HTML and its `data-page` JSON holds only translations, so rows come from a separate Inertia request |
| Svns_Standings.html | https://www.svns.com/en/standings | rendered client-side; no ranking data in the HTML. Embeds `seriesIds":[{"id":1574166` for the World Rugby pulselive API |

Replacement sources found on 2026-09-15 (endpoints located by the project owner in a browser, verified from a scripted client):

| File | Source URL | Notes |
|---|---|---|
| Fifa_V3_Men_FRS_20260611.json | https://api.fifa.com/api/v3/fifarankings/rankings/rankingsbyschedule?rankingScheduleId=FRS_Male_Football_20260611&language=en | current FIFA API; `Results[].{Rank, IdCountry}`; serves the new `FRS_*` ids that ranking-overview does not |
| Fifa_V3_Women_FRS_20260419.json | same endpoint with `FRS_Female_Football_20260419` | same shape |
| Fifa_WorldRanking_Women.html | https://inside.fifa.com/fifa-rankings/world-ranking/women | its own `ranking.dates` holds the `FRS_Female_*` ids |
| Bwf_MensSingles.json | https://extranet-lv.bwfbadminton.com/api/vue-rankingtable?rankId=2&catId=6&publicationId=0&doubles=false&searchKey=&pageKey=100&page=1 | Laravel-style pagination (`results.{data,total,per_page,last_page}`); `pageKey=2000` is honored; country is a name only (`p1_country_model.name`), no code |
| Bwf_MensDoubles.json | same with `catId=8&doubles=true` | two players, each with their own country; pairs can be mixed-nationality |
| Wbsc_Baseball_Men.json | https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-m&date=2026-03-26&fullView=1&preview=&lang=en | `rankings[].{position, ioc}`; the `date` must be an exact release date or `rankings` is empty |
| Wbsc_Rankings.html | https://rankings.wbsc.org/ | embeds the release-date list as `{"date","sport","year","formatted"}` objects; take the max date per `sport` id |
| Svns_Standings.html | https://www.svns.com/en/standings | `data-series-ids="<men guid>,<women guid>"` on the `series-standings` section; ids change each season |
| Svns_Series_Men.json / _Women.json | https://api.wr-rims-prod.pulselive.com/rugby/v3/series/{id} | `sport` is `mrs` (men) or `wrs` (women) |
| Svns_Standings_Men.json / _Women.json | https://api.wr-rims-prod.pulselive.com/rugby/v3/series/{id}/standings | `entries[].{position, team.abbreviation}`; `team.countryCode` is null |

Gymnastics, captured on 2026-09-16 (the sport was dropped in the 2026-09-15 refactor and restored the next day):

| File | Source URL | Notes |
|---|---|---|
| Fig_Artistic_Men.html | https://www.gymnastics.sport/site/rankings/ranking_mag_table.php | fragment loaded by ranking_mag.php; two series (Apparatus World Cup, World Challenge Cup) with one tab per apparatus; rows are athletes with the IOC code in the flag `img` alt. No ranking date. The "By Country" all-around section is inside an HTML comment and frozen at 2020 |
| Fig_Artistic_Women.html | https://www.gymnastics.sport/site/rankings/ranking_wag_table.php | same shape, four apparatus |
| Fig_Rhythmic_Women.html | https://www.gymnastics.sport/site/rankings/ranking_rg_table.php | same shape; the three Group tabs rank national groups and carry the country name instead of an athlete name |
| Bwf_MensSingles_RepeatedRows.json | the men's singles URL with `pageKey=2000`, 2026-09-18; **excerpt**: data rows 938-942 inside the original envelope | five consecutive rows tied at rank 925 holding three players, two of them listed twice (a new row `id`, the same `player1_id`, rank and points). The full lists echo rows like this inside tie blocks: 172 echoes over the five lists that day |
| Bwf_MensDoubles_TiedPairs.json | the men's doubles URL with `pageKey=2000`, 2026-09-18; **excerpt**: the two rows of `player1_id` 88711 at rank 505 | one player in two different pairs tied on rank and points; he is ranked with six partners in all |
| Wikipedia_IihfWorldRanking.html | https://en.wikipedia.org/w/rest.php/v1/page/IIHF_World_Ranking/html (2026-09-18, descriptive User-Agent) | Parsoid HTML; a `<section>` per h2, `id="Men's_rankings"` and `id="Women's_rankings"`, each with one `wikitable sortable`: current rank, previous rank, team link, ..., current total in `<b>`. Unranked teams show `NR` (men: Russia, Belarus, India, Morocco) or `new` (women: Philippines). No ranking date, only "Jun 2026" in the column header |

Still not capturable: the ATP doubles page (Cloudflare challenge, 403 even with `?rankRange=0-5000`). iihf.com answers every scripted client, curl included, with a Cloudflare challenge (`Cf-Mitigated: challenge`), and https://www.iihf.com/en/worldranking now redirects to a static page; the ranking is read from Wikipedia instead.

BWF caveat: every BWF fixture was captured with curl. The same URL returns 403 ("Sorry, you have been blocked") to .NET `HttpClient` through `SocketsHttpHandler` or `WinHttpHandler`, on TLS 1.2 and 1.3 alike, with a browser or a descriptive User-Agent, although the request bytes are identical to curl's; what remains is the TLS handshake signature. curl passes with a browser or a descriptive User-Agent and is refused with `curl/8.x` or none (2026-09-18), so the five feeds are fetched through `CurlFetcher`. serviceconfig.json asks for `pageKey=2000` (honored per the note above); the two whole-list fixtures remain the `pageKey=100` captures. Men's singles holds 2,140 players, so the last 140 wait for the multi-page fetch.
