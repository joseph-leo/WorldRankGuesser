# SportsRankingService

Scrapes world team rankings (FIFA, ICC, World Rugby, FIH, WBSC, FIBA, tennis, volleyball, ...) from
public federation feeds and stores them in SQL Server as immutable *releases*: a new release is
written only when a feed's content or federation ranking date changes, so the database holds both
the current table per feed and its history.

## Setup

```powershell
docker compose up -d --wait                              # SQL Server 2022 on localhost,1433, loopback-only (sa / Rankings_Dev1!)
dotnet tool restore                                      # dotnet-ef
dotnet ef database update --project SportsRankingService --startup-project SportsRankingService # create the database and schema
dotnet run --project SportsRankingService                # fetch every enabled feed once and exit
dotnet run --project SportsRankingService -- --only "Cricket ODI Women" --only Soccer   # rerun a subset
dotnet test SportsRankingService.Tests                   # fixture-based tests, no network or database
```

The process exits 0 when every enabled feed was saved and 1 when any feed failed, so a scheduler's
"last run result" is meaningful.

`--only` (repeatable) selects feeds by the name the log prints, "Sport Event Gender": every word of the
pattern must appear in the name, in order, as a whole word. `Cricket` runs every cricket feed,
`Cricket ODI` both ODI feeds, `Cricket Women` every women's cricket feed, `Gymnastics` all eighteen
gymnastics feeds and `Cricket ODI Women` one. A pattern that matches no enabled feed is an error and
nothing is fetched; disabled feeds stay disabled. Copy the name from a "Feed ... failed" line to rerun
just that feed.

## Scheduling a weekly run

Windows Task Scheduler (after `dotnet publish -c Release -o publish`):

```powershell
schtasks /Create /SC WEEKLY /D MON /ST 06:00 /TN SportsRankings /TR "C:\path\to\publish\SportsRankingService.exe"
```

cron:

```
0 6 * * 1 /path/to/publish/SportsRankingService
```

## Reading the data

`CurrentRankings` is a view with one row per entry of each feed's newest release; `CurrentCountryRankings` collapses it to one row per country (the best-placed entry plus its entrant count).
`CurrentRankings` columns: Sport, Event, Gender, RankingDate, IsFederationDate, Ordinal, Position, ISO3, TeamName, Competitor, Points; `CurrentCountryRankings` columns: Sport, Event, Gender, RankingDate, IsFederationDate, Position, ISO3, TeamName, Competitor, Points, RankedEntrants.
`ISO3` identifies the country and `TeamName` is one fixed display name per code (`SportsRankingService/Utilities/CountryNames.cs`), the same in every sport, never the federation's own spelling.
`RankingReleases` and `RankingRows` hold every release ever seen; `IsFederationDate` says whether
`RankingDate` came from the federation or is the scrape date of a feed that publishes none. For a
dateless feed, `RankingDate` is the day its content was first seen and does not move while that
content stays the same; use `LastSeenAt` on `RankingReleases` for "as of".

## Configuration

`serviceconfig.json` lists the feeds; `appsettings.json` holds the connection string. Override it
outside development with the `ConnectionStrings__WorldRankGuesserConnection` environment variable.
