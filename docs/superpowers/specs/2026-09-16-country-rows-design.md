# Country rows: one row per country, competitor and points on every row

Date: 2026-09-16. Status: approved design (in chat), implemented on branch `persistence-redesign`.

## Goal

The consumer game asks "in which sport is this country rated highest?". Every stored
ranking must therefore be a ranking *of countries*, whatever the federation ranks:

1. One row per country per release. For athlete rankings (tennis, badminton, gymnastics)
   the row is the country's best-placed athlete or pair; team rankings are unchanged.
2. `TeamName` is always the country name: the federation's spelling when the feed gives
   one, otherwise the English region name derived from the ISO3 code.
3. Two new facts per row for flavour and for other games: `Competitor` (the athlete, pair
   or group the country's row stands on; null for team sports) and `Points` (the
   federation's headline points or rating, on that federation's own scale).
4. `RankedEntrants`: how many of the country's athletes or pairs appear in the ranking
   (1 for team sports), computed while collapsing to countries.
5. Gymnastics stores only the FIG World Cup series, and its `Event` is the apparatus name.

Out of scope, decided with the user: previous position and movement (derivable from our
release history), confederation, matches played, athlete details, raw response archive.

## Decisions taken with the user

| Question | Decision |
|---|---|
| Athlete rankings: every athlete, or one row per country? | One row per country, the best-placed athlete. |
| Stored position for that row | The federation's published position, not a re-numbering among countries. The consumer can dense-rank. |
| World Challenge Cup series | Dropped. World Cup only (18 feeds). |
| Event name for gymnastics | The apparatus alone ("Vault", "Group 5x"); the series is not part of the name. |
| Extra fields | `Points` and `RankedEntrants` yes; confederation and previous position no. |
| Dev database | Recreated once (`docker compose down -v`) because it holds releases under the old event names and with athlete rows. The single `InitialSchema` migration is regenerated rather than followed by a second migration, since nothing has been deployed. |

## Pipeline

- `RankEntry(Position, ISO3, TeamName?, Competitor?, Points?)` is still what a parser
  emits, one per ranked entity, with no country collapse in the parser. Parsers that
  rank people put the person's name in `Competitor` and leave `TeamName` to the country
  name when the feed has one.
- `RankingSnapshotBuilder` orders by position, keeps the first entry per ISO3 (ties by
  feed order), counts the country's entries as `RankedEntrants`, fills a missing
  `TeamName` from `CountryUtil.GetCountryName`, then applies `Take` by position. Its
  output entries are `RankingSnapshotEntry(Position, ISO3, TeamName?, Competitor?,
  Points?, RankedEntrants)`.
- The content hash covers every stored column, so a change of best athlete or of points
  is a new release.

## Persistence

`RankingRows` gains `Competitor nvarchar(100) null`, `Points decimal(12,3) null` and
`RankedEntrants int not null`. The `CurrentRankings` view projects the three new columns.
Per CLAUDE.md the view is raw SQL in the migration and is maintained by hand.

## Points per feed

| Feed | Field |
|---|---|
| FIFA | `TotalPoints` |
| World Rugby | `pts` |
| SVNS | `totalPoints` |
| ICC | `Rating` (the ranking metric; `Points` is the raw total) |
| FIH | `points` |
| Volleyball World | `decimalPoints` (indoor), `points` (beach, where rows are pairs and `name` is the competitor) |
| WBSC | `points` |
| FIBA | the PTS column |
| ESPN tennis | `points` |
| WTA | `points` |
| BWF | `points` |
| FIG | the Total column |
