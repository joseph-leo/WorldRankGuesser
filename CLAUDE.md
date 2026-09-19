# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Blazor Server game. The player is shown a random country and assigns it to one of 10 sport categories; the app then looks up that country's best world ranking in that sport by scraping live ranking sources. After all 10 categories are filled, the positions are summed — lower total is better.

## Commands

```powershell
dotnet build WorldRankGuesser.sln
dotnet run --project WorldRankGuesser                        # http://localhost:5170
dotnet run --project WorldRankGuesser --launch-profile https # https://localhost:7296
dotnet watch --project WorldRankGuesser                      # hot reload
```

There is no test project and no lint configuration. The build emits ~60 nullable/BL0016 warnings; these are pre-existing.

Single project (`WorldRankGuesser/WorldRankGuesser.csproj`), classic Blazor Server hosting model (`AddServerSideBlazor` / `MapBlazorHub` / `_Host.cshtml`), not the .NET 8+ Blazor Web App model. Dependencies: HtmlAgilityPack (HTML scraping) and Newtonsoft.Json (JSON sources; `System.Text.Json` is used for config and session storage).

## Architecture

### Ranking pipeline: `urls.json` → `ScrapeService<TRank, TRankModel>` → `Rank`

`Services/ScrapeService.cs` is the abstract base for every sport. The pieces are wired together by naming convention rather than DI:

- `wwwroot/urls.json` is a three-level map: **Rank class name** (`"HockeyRank"`) → **sport variant** (`"Field Hockey"`, `"Ice Hockey"`) → **gender** (`"Men"`, `"Women"`, or `"Both"`) → URL. The base class looks up its section with `typeof(TRank).Name`, so the top-level key must exactly match the `Data/*Rank.cs` class name.
- A URL value starting with `wwwroot` is read from disk instead of fetched. Several sources (gymnastics, women's field hockey) are saved browser snapshots under `wwwroot/remotehtml/`; they only update when someone re-saves the page. These paths, and `GeneralUtil.ReadConfig`, are relative to the process working directory, which must be the project directory (`dotnet run` sets this; running the DLL from `bin/` does not).
- The base class iterates variant × gender, sets the mutable `Sport` and `Gender` properties, then calls the subclass's `ParseRanks(response)`. Subclasses read `Sport`/`Gender` to tag results and — when one service covers structurally different sources — to `switch` between parsers (see `HockeyService`, `RugbyService`). When the gender key is `"Both"`, the parser derives gender from the response itself.
- `GetLowestRankAsync(iso3)` returns the numerically lowest `Position` across all variants and genders for a country. A country missing from a source gets a placeholder rank with `Position = 200`; `Rank.Unranked` is defined as `Position == 200`, so 200 is a sentinel — don't change one without the other.
- Override points beyond `ParseRanks`: `GetCountryRank` (`CricketService` maps the 15 West Indies member countries onto the single `"WI"` entry) and `CallUrlAsync` (`TennisService` delays each call to stay under the Sportradar trial rate limit).
- `TRankModel` is currently unused; every service passes the same type twice.

### Country identity

Everything is keyed on ISO 3166 alpha-3 codes from .NET `RegionInfo` (`CountryUtil.GetCountries()` enumerates all specific cultures). Sources disagree on how they name countries, so `Helpers/CountryUtil.cs` normalizes:

- `IOCToISO3` — IOC/FIFA-style codes (`GER`, `NED`, `RSA`) → ISO3; unknown codes pass through unchanged.
- `GetISO3FromCountry` / `GetRegionMapping` — display names → `RegionInfo.EnglishName` (England/Scotland/Wales/NI → United Kingdom, Chinese Taipei → Taiwan, etc.). This throws a `NullReferenceException` on an unmapped name, which is the usual failure when a source adds or renames a team; the fix is a new `case` in `GetRegionMapping`.
- Flags are emoji computed from the ISO2 code, not image assets.

### Game flow (`Pages/Game.razor` + `.razor.cs`)

- The country list is shuffled and "spun" through 50 entries (50 ms each) before landing on one; `loading` disables the cards during the spin. The chosen country is removed from the pool.
- Each `CategoryCard` invokes `GetRankAsync<TService, TRank, TModel>`, which instantiates the service with `Activator.CreateInstance` — services are not registered in DI. The `CardFlags` dictionary key is the rank type name minus `"Rank"`, and must equal the card's `Sport` parameter string for the card to flip to a flag.
- The category count is hardcoded (`CardFlags.Count < 10`, `Rankings.Count == 10`).
- On the 10th pick, results are serialized as `List<Rank>` into `sessionStorage` and the app navigates to `/rankings` with `forceLoad: true`; `RankingsPage` reads and clears the key in `OnAfterRenderAsync` (JS interop is unavailable earlier). Because the list is typed as the base class, subclass-only properties such as `GymnasticsRank.Event` do not survive the round trip.

### Adding a sport

1. `Data/FooRank.cs` deriving from `Rank`.
2. `Services/FooService.cs` deriving from `ScrapeService<FooRank, FooRank>` with a `ParseRanks` implementation.
3. A `"FooRank"` section in `wwwroot/urls.json`.
4. A `<CategoryCard Sport="Foo" …>` in `Game.razor`, and bump the two hardcoded `10`s in `Game.razor.cs`.

## Notes

- `Pages/Counter.razor`, `Shared/SurveyPrompt.razor`, and `NavMenu` are leftovers from the Blazor template.
- The `<None Include="wwwroot\remotehtml\…">` list in the csproj is IDE-generated noise; snapshot files do not need to be listed there.
