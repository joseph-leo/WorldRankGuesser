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
| VolleyballWorld_Men.json | https://en.volleyballworld.com/api/v1/worldranking/volleyball/1/0/100 | parses |
| Icc_T20_Women.json | https://assets-icc.sportz.io/cricket/v1/ranking?...&comp_type=t20w&type=team | parses; `shortname` carries a `-W` suffix |
| Icc_Test_Men.json | https://assets-icc.sportz.io/cricket/v1/ranking?...&comp_type=test&type=team | parses; two-letter codes SA, NZ, SL, WI |
| Fifa_Overview_Men_id14870.json | https://inside.fifa.com/api/ranking-overview?locale=en&dateId=id14870 | parses; the API returns an empty list for the newer `FRS_*` date ids |
| Fifa_WorldRanking_Men.html | https://inside.fifa.com/fifa-rankings/world-ranking/men | page data still embeds the ranking dates |
| Fiba_Ranking_Men.html | https://www.fiba.basketball/rankingmen (redirects to /fr/ranking/men) | markup changed: no IOC column, country only in the team link slug |
| Wbsc_Rankings.html | https://rankings.wbsc.org/ | markup changed: the ranking table is empty in the HTML and its `data-page` JSON holds only translations, so rows come from a separate Inertia request |
| Svns_Standings.html | https://www.svns.com/en/standings | rendered client-side; no ranking data in the HTML. Embeds `seriesIds":[{"id":1574166` for the World Rugby pulselive API |

Not capturable on 2026-09-15: BWF (`bwfshuttleapi.com` unreachable), IIHF world ranking (403), ATP doubles page (403).
