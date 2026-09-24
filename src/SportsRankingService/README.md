# SportsRankingService

Scrapes world team rankings (FIFA, ICC, World Rugby, FIH, WBSC, FIBA, tennis, volleyball, ...) from
public federation feeds and stores them in SQL Server as immutable *releases*: a new release is
written only when a feed's content or federation ranking date changes, so the database holds both
the current table per feed and its history.

## Setup

```powershell
docker compose up -d --wait                              # SQL Server 2022 on localhost,1433, loopback-only (sa / Rankings_Dev1!); the repo root's compose file
dotnet tool restore                                      # dotnet-ef
dotnet ef database update --project src/SportsRankingService   # create the database and the dbo schema
dotnet run --project src/SportsRankingService            # fetch every enabled feed once and exit
dotnet run --project src/SportsRankingService -- --only "Cricket ODI Women" --only Soccer   # rerun a subset
dotnet test tests/SportsRankingService.Tests             # fixture-based tests, no external network or database (needs curl on the PATH)
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

As a container (`src/SportsRankingService/Dockerfile`, built from the repo root; it carries curl and runs as a
non-root user; the exit code is the process's, so a scheduler or a Container Apps Job sees a failed feed):

```powershell
docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
docker run --rm -e "ConnectionStrings__WorldRankGuesserConnection=Server=...;Database=WorldRankGuesser;..." worldrankguesser-scraper                # every enabled feed
docker run --rm -e "ConnectionStrings__WorldRankGuesserConnection=..." worldrankguesser-scraper --only Soccer   # a subset
```

In Azure the same image runs as the Container Apps Job `caj-wrg-<env>-scraper`, scheduled by `infra/scraper.bicep` (Fridays
06:00 UTC on staging, Mondays in production) and deployed by `.github/workflows/deploy-scraper.yml`; `infra/README.md` is
the runbook, and `scraper-check.yml` fails a run when the weekly scrape did not succeed.

In this repo's compose stack it is `docker compose run --rm scraper`.

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

A feed whose list comes in pages puts `{page}` in its URL and, when the API counts from 0, says
`"FirstPage": 0`: the pages are fetched one after another until one yields no entries, and a page that
cannot be fetched fails the feed rather than storing a shorter list. WTA doubles is the one such feed
(the API caps a page at 100 players). Volleyball World and ESPN offer no paging, so their depth is
whatever one request returns.

A feed is fetched with .NET's `HttpClient` unless its item says `"Fetcher": "Curl"`, which runs the
machine's `curl` instead. Seven feeds need it: the five BWF badminton feeds, because BWF's Cloudflare rule
refuses .NET's TLS handshake yet answers curl, and the two ice hockey feeds, because Wikimedia asks
scripts to send a descriptive User-Agent, which only the Curl fetcher does. `curl` must therefore be on
the PATH: Windows 10 and later, macOS and most Linux distributions ship it (slim container images may
not; the Dockerfile installs it). curl is started directly, never through a shell, so the same code runs
on all three; without it only those seven feeds fail, each with a log line that says so.

A third fetcher, `"Fetcher": "Proxy"`, sends the request to the Cloudflare Worker in `proxy/` (`GET /fetch?url=...`
with the shared token in `X-Proxy-Token`), which forwards it from Cloudflare's addresses and returns the upstream
answer unchanged. The five WBSC feeds need it: www.wbsc.org sits behind CloudFront, which refuses hosting addresses
(Azure's Job got 403 for every client on 2026-09-24) but serves Cloudflare. The Worker is configured by `Proxy__Url`
and `Proxy__Token` (the Job sets them from `infra/scraper.bicep`); with no URL configured, as in a local run, those
feeds are fetched directly with one warning, which works from a residential address. A resolver's preliminary
request goes through the item's fetcher too, so the WBSC release-date page takes the same route as its feeds.

Ice hockey comes from Wikipedia's "IIHF World Ranking" article rather than iihf.com, which answers
every scripted client with a Cloudflare challenge. The article states no ranking date, so those two
feeds carry the scrape date (`IsFederationDate = 0`).
