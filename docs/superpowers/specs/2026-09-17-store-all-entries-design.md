# Store every parsed entry: rows are entries, countries are derived

Date: 2026-09-17. Status: approved design (in chat), to be implemented on branch
`persistence-redesign`.

## Goal

The store must not decide what consumers need. Every entry a feed publishes is stored,
one row per ranked entity — athlete, doubles partner, pair, group or team — and
country-level facts (best entry, entrant counts) are derived in read models. Apps that
want a top 150 take it themselves.

This supersedes the country collapse of `2026-09-16-country-rows-design.md`. That
spec's goal — every ranking queryable as a ranking of countries — survives, moved from
the writer (`RankingSnapshotBuilder`) into a derived view.

## Decisions taken with the user

| Question | Decision |
|---|---|
| Scope | Remove the collapse and store everything now; include URL-only page-size raises. Multi-page fetching (ESPN tennis depth) is a separate future spec because it changes the fetch pipeline. |
| Duplicate entries | Parsers must not emit equal entries. The builder throws `ParseException` when any two snapshot entries compare equal (record equality), for every feed. |
| BWF doubles | One row per partner regardless of country; `Competitor` becomes the partner's own name, no longer the pair string. A same-country pair is two rows differing only in `Competitor`, so the guard is satisfied by construction. |
| Read model | Two views: `CurrentRankings` (all rows of the newest release per feed) and `CurrentCountryRankings` (one row per country: its best-placed entry plus a derived `RankedEntrants`). |
| `Take` | Deleted from `RankingItem`. It was never set in `serviceconfig.json`; the real caps were URL parameters. |
| Migration | Regenerate `InitialSchema` and recreate the dev database (`docker compose down -v`), as for the country-rows change, because nothing is deployed. |

## Pipeline

- `RankEntry` is unchanged: one per ranked entity, no collapse in parsers.
- `RankingSnapshotBuilder` keeps the stable sort by position (ties keep feed order),
  the `TeamName` fill from `CountryUtil.GetCountryName`, and the date choice (parser's
  date, else resolver's, else today). It loses the country collapse, `Take` and
  `RankedEntrants`. `RankingSnapshotEntry` becomes
  `(Position, ISO3, TeamName, Competitor, Points)`.
- Duplicate guard: after the `TeamName` fill, if any two entries compare equal the
  builder throws `ParseException` naming the entry. Records make the check plain
  equality. A genuine coincidence — two same-named, same-country entries tied on
  position and points — would fail that feed's run; accepted as vanishingly unlikely
  and preferable to silent duplication.
- The content hash loses its `RankedEntrants` term and is otherwise unchanged. Every
  feed re-releases once after this change, which is moot because the database is
  recreated.

## Parser fixes

Two parsers emit duplicates today that the collapse absorbs silently:

- **Fiba**: the page holds three tables — the ranking plus "biggest movers" and
  "biggest drops" widgets whose rows also carry ranks, so `//table//tbody/tr` sweeps
  169 rows of which only 159 are distinct in the men's fixture. Scope row selection to
  the main ranking table; the fixture test pins 159 entries, all distinct.
- **Bwf**: doubles rows yielded one entry per partner with the pair string as
  `Competitor`, so a same-country pair produced two identical entries. Each partner's
  entry now carries that partner's own display name; everything else (pair position,
  pair points, one entry per partner, neutral athletes dropped) is unchanged.

## Persistence

`RankingRows` holds one row per parsed entry; `Ordinal` remains the storage order
after the stable sort; the `RankedEntrants` column is dropped; nothing else changes.
A country may appear many times per release.

Both views are raw SQL in the regenerated `InitialSchema`, outside the EF model, with
the existing discipline: a later migration that renames or retypes a projected column
must drop and re-create them itself.

- `CurrentRankings`: all rows of the newest release per feed — as today, minus
  `RankedEntrants`.
- `CurrentCountryRankings`: for the newest release per feed, one row per country: the
  row with the lowest `Position` (ties by lowest `Ordinal`) plus
  `RankedEntrants = COUNT(*)` of the country's rows in that release. This reproduces
  the previous stored shape, including counting both partners of a same-country pair,
  and keeps the collapse rule defined in exactly one place.

## Page sizes (URL-only raises)

Live probes on 2026-09-17, while planning:

- WTA silently caps `pageSize` at 100 (2000 and 200 both return 100 rows; `page=1`
  returns positions 101 on), so the raise this spec assumed does not exist. The feed
  stays at `pageSize=100` with a config note; its full depth joins the multi-page spec.
- Volleyball World serves at most 500 rows in one request (600 and above return an
  error page). All four feeds move from 100 to 500. The indoor lists are complete at
  that size (148 men, 133 women on probe day); the beach lists fill all 500 rows, so
  their full depth also joins the multi-page spec.
- BWF: `pageKey=150` raised to 2000, which `FIXTURES.md` records as honored, although
  the feeds stay disabled (Cloudflare 403).
- ESPN tennis has no size parameter; deeper tennis lists wait for the multi-page spec.

The two Volleyball World fixtures are recaptured at the new URLs — the configured URL
is part of what `FIXTURES.md` records — and their test counts updated.

## Testing

- Builder tests: collapse and `Take` tests replaced by no-collapse and duplicate-guard
  tests; `TeamName` fill and date-choice tests unchanged in spirit.
- `FibaParserTests`: 159 entries, all distinct.
- `BwfParserTests`: doubles rows carry each partner's own name; a same-country pair
  yields two rows differing only in `Competitor`.
- Repository (SQLite) and `RankingSourceRunner` tests updated for the new entry shape.
- The views stay verified against the real server only; SQLite never sees them.

## Documentation

CLAUDE.md (persistence, architecture, current state) and the README's row-count
expectations are updated in the same change.

## Out of scope

Multi-page fetching, position re-numbering, any consumer app, and the disabled feeds'
other blockers (Cloudflare, IIHF source).
