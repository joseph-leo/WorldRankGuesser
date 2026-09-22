# Persistence Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the reverse-engineered full-copy persistence with a Docker-hosted SQL Server, a code-first schema of immutable ranking releases (inserted only when a feed's content or federation date changes), federation ranking dates flowing through the pipeline, and a run-once console entry point.

**Architecture:** The fetch/parse/stamp pipeline gains a ranking date (`ParsedRanking`, `ResolvedUrl`) and ends in a plain `RankingSnapshot` record. A new `Persistence/` folder owns EF Core: two entities (`RankingRelease`, `RankingRow`), a `RankingsDbContext`, an `IRankingRepository` that hashes a snapshot and inserts a release only when the hash differs from the feed's newest one, and migrations that also create a `CurrentRankings` view. `Program.cs` becomes a console `Main` that runs every enabled feed once and returns an exit code.

**Tech Stack:** .NET 8, EF Core 8.0.31 (SqlServer, Design, Sqlite for tests), `dotnet-ef` 8.0.31 as a repo-local tool, `Microsoft.Extensions.Hosting` 8.0.1, `Microsoft.Extensions.TimeProvider.Testing` 8.10.0, xUnit, Docker Compose with `mcr.microsoft.com/mssql/server:2022-latest`.

**Spec:** `docs/superpowers/specs/2026-09-15-persistence-redesign-design.md`

## Global Constraints

- Target framework stays `net8.0`; SDK is pinned by `global.json` (8.0.0, `rollForward: latestMajor`; the machine has 11.0 RC, which is fine).
- EF Core packages are exactly `8.0.31`; `dotnet-ef` is `8.0.31`; `Microsoft.Extensions.Hosting` is `8.0.1`.
- Nothing under `Parsing/`, `Parsers/`, `Services/`, `Models/` or `Configuration/` may reference `Microsoft.EntityFrameworkCore`. Only `Persistence/` and `Program.cs` do.
- Dev SA password is `Rankings_Dev1!` in both `docker-compose.yml` and `appsettings.json`.
- Connection string name stays `WorldRankGuesserConnection`; database name stays `WorldRankGuesser`.
- Table names: `RankingReleases`, `RankingRows`; view: `CurrentRankings`. `ISO3` is `varchar(3)`, not fixed length.
- "Newest release for a feed" is the greatest `Id`, never an ordering by `FirstSeenAt` (SQLite cannot order by `DateTimeOffset`, and identity order is the same thing).
- Every commit builds with 0 errors (`dotnet build SportsRankingService.sln`) and `dotnet test SportsRankingService.Tests` passes except the three deliberately skipped tests in `MissingSourcesTests`.
- Commit messages: imperative sentence, no prefix, and end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- The working tree already has staged deletions of superseded files from the previous refactor. Leave them staged; they get committed in Task 11. Use `git commit -- <paths>` (pathspec form) in earlier tasks so those deletions are not swept in early.
- All commands run from the repo root `C:\Users\Josep\OneDrive\Documents\GitHub\SportsRankingService` unless noted.

---

### Task 1: Docker Compose SQL Server and connection string

**Files:**
- Create: `docker-compose.yml`
- Modify: `SportsRankingService/appsettings.json`
- Modify: `SportsRankingService/Program.cs` (one line: stop appending `;Encrypt=False`)

**Interfaces:**
- Produces: a SQL Server reachable at `localhost,1433`, user `sa`, password `Rankings_Dev1!`; connection string key `ConnectionStrings:WorldRankGuesserConnection`.

- [ ] **Step 1: Write `docker-compose.yml`**

```yaml
# Local SQL Server for development. Start with `docker compose up -d --wait`.
# The SA password is a dev-only credential for this container; it is also in
# SportsRankingService/appsettings.json. Override the connection string elsewhere with the
# ConnectionStrings__WorldRankGuesserConnection environment variable.
services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: worldrankguesser-sql
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Rankings_Dev1!"
      MSSQL_PID: Developer
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -Q 'SELECT 1' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s

volumes:
  sqldata:
```

- [ ] **Step 2: Replace the connection string in `SportsRankingService/appsettings.json`**

```json
{
  "ConnectionStrings": {
    "WorldRankGuesserConnection": "Server=localhost,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True"
  },
  "Worker": {
    "Interval": "1.00:00:00"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

(The `Worker` section is removed in Task 9.)

- [ ] **Step 3: Stop appending `;Encrypt=False` in `Program.cs`**

Change

```csharp
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection") + ";Encrypt=False"));
```

to

```csharp
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection")));
```

- [ ] **Step 4: Start the container and confirm it is healthy**

Run: `docker compose up -d --wait`
Expected: exits 0 and prints the `sql` service as `Healthy`. If port 1433 is already taken by a local SQL instance, stop that service or change the mapping to `"14333:1433"` and the connection string to `localhost,14333`.

Run: `docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -Q "SELECT @@VERSION"`
Expected: a line starting with `Microsoft SQL Server 2022`.

- [ ] **Step 5: Build and commit**

Run: `dotnet build SportsRankingService.sln`
Expected: `0 Error(s)`.

```bash
git add docker-compose.yml SportsRankingService/appsettings.json SportsRankingService/Program.cs
git commit -m "Run SQL Server in Docker Compose for local development" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- docker-compose.yml SportsRankingService/appsettings.json SportsRankingService/Program.cs
```

---

### Task 2: EF Core 8 and the dotnet-ef local tool

**Files:**
- Modify: `SportsRankingService/SportsRankingService.csproj`
- Create: `.config/dotnet-tools.json`

**Interfaces:**
- Produces: `dotnet ef` available after `dotnet tool restore`; EF Core 8.0.31 with native `DateOnly` support.

- [ ] **Step 1: Bump packages in `SportsRankingService/SportsRankingService.csproj`**

Replace the package `ItemGroup` with:

```xml
  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.54" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.31" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.31">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
```

- [ ] **Step 2: Create the tool manifest**

Run: `dotnet new tool-manifest` (creates `.config/dotnet-tools.json`)
Run: `dotnet tool install dotnet-ef --version 8.0.31`
Expected: `.config/dotnet-tools.json` now contains:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "8.0.31",
      "commands": [
        "dotnet-ef"
      ]
    }
  }
}
```

- [ ] **Step 3: Verify the tool and the build**

Run: `dotnet tool restore` then `dotnet ef --version`
Expected: `Entity Framework Core .NET Command-line Tools 8.0.31`.

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all tests pass except 3 skipped.

- [ ] **Step 4: Commit**

```bash
git add .config/dotnet-tools.json SportsRankingService/SportsRankingService.csproj
git commit -m "Upgrade to EF Core 8 and pin dotnet-ef as a local tool" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- .config/dotnet-tools.json SportsRankingService/SportsRankingService.csproj
```

---

### Task 3: Parsers return `ParsedRanking`

**Files:**
- Create: `SportsRankingService/Parsing/ParsedRanking.cs`
- Create: `SportsRankingService/Parsing/IsoDate.cs`
- Modify: `SportsRankingService/Parsing/IRankingParser.cs`
- Modify: `SportsRankingService/Parsing/JsonRankingParser.cs`
- Modify: `SportsRankingService/Parsing/HtmlRankingParser.cs`
- Modify: `SportsRankingService/Services/RankingSourceRunner.cs` (use `.Entries`)
- Modify: every file in `SportsRankingService.Tests/Parsers/` that calls `Parse(Fixture.Read(...))`: Bwf, EspnTennis, Fiba, FifaV3, Fih, Icc, Svns, VolleyballWorld, Wbsc, WorldRugby, Wta
- Test: `SportsRankingService.Tests/Parsing/IsoDateTests.cs` (create)

**Interfaces:**
- Produces: `ParsedRanking(IReadOnlyList<RankEntry> Entries, DateOnly? RankingDate = null)`; `IRankingParser.Parse(string) : ParsedRanking`; `JsonRankingParser<TRoot>.GetRankingDate(TRoot) : DateOnly?` (virtual, null); `HtmlRankingParser.GetRankingDate(HtmlDocument) : DateOnly?` (virtual, null); `IsoDate.Parse(string sourceName, string? text) : DateOnly`.

- [ ] **Step 1: Write the failing `IsoDate` test**

Create `SportsRankingService.Tests/Parsing/IsoDateTests.cs`:

```csharp
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsing;

/// <summary>Feeds write their ranking date as an ISO date or timestamp; only the calendar date is kept.</summary>
public class IsoDateTests
{
    [Theory]
    [InlineData("2026-09-12", 2026, 9, 12)]
    [InlineData("2026-09-14T00:00:00Z", 2026, 9, 14)]
    [InlineData("2026-09-10T07:00Z", 2026, 9, 10)]
    [InlineData("2026-09-01T00:00:00.000Z", 2026, 9, 1)]
    public void Keeps_the_calendar_date_of_an_iso_value(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), IsoDate.Parse("Test", text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12/09/2026")]
    [InlineData("9/15/2026 6:53:21 PM")]
    public void Anything_else_fails_the_parse_naming_the_value(string? text)
    {
        var ex = Assert.Throws<ParseException>(() => IsoDate.Parse("Test", text));

        Assert.Contains("Test", ex.Message);
        Assert.Contains(text ?? "null", ex.Message);
    }
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~IsoDateTests"`
Expected: build error, `IsoDate` does not exist.

- [ ] **Step 3: Add `ParsedRanking` and `IsoDate`**

Create `SportsRankingService/Parsing/ParsedRanking.cs`:

```csharp
namespace SportsRankingService.Parsing;

/// <summary>
/// What a parser extracts from one feed response: the ranked entries and, when the feed publishes
/// one, the date the federation attached to the ranking. Null means the feed carries no usable
/// ranking date (some only stamp the time the file was generated).
/// </summary>
public sealed record ParsedRanking(IReadOnlyList<RankEntry> Entries, DateOnly? RankingDate = null);
```

Create `SportsRankingService/Parsing/IsoDate.cs`:

```csharp
using System.Globalization;

namespace SportsRankingService.Parsing;

/// <summary>Reads the calendar date at the start of an ISO 8601 date or timestamp ("2026-09-12", "2026-09-12T07:00Z").</summary>
public static class IsoDate
{
    public static DateOnly Parse(string sourceName, string? text)
    {
        if (text is { Length: >= 10 }
            && DateOnly.TryParseExact(text.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date;
        }

        throw new ParseException(sourceName, $"ranking date '{text ?? "null"}' is not an ISO date");
    }
}
```

- [ ] **Step 4: Run the `IsoDate` tests**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~IsoDateTests"`
Expected: 8 passed.

- [ ] **Step 5: Change the parser contract**

`SportsRankingService/Parsing/IRankingParser.cs`:

```csharp
namespace SportsRankingService.Parsing;

/// <summary>
/// Turns one feed response into a ranking. A parser is a pure function of the response text:
/// it knows nothing about which sport, event or gender it is being used for.
/// Implementations are registered as <see cref="IRankingParser"/> singletons and indexed by
/// <see cref="SourceName"/>, which is the value a <c>RankingItem.Source</c> in serviceconfig.json refers to.
/// </summary>
public interface IRankingParser
{
    string SourceName { get; }

    /// <exception cref="ParseException">The response does not have the expected shape.</exception>
    ParsedRanking Parse(string response);
}
```

`SportsRankingService/Parsing/JsonRankingParser.cs`:

```csharp
using System.Text.Json;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return JSON. Subclasses declare a small DTO type holding only the
/// properties they need (unknown JSON properties are ignored), map it to entries, and may
/// override <see cref="GetRankingDate"/> when the feed publishes a ranking date.
/// </summary>
public abstract class JsonRankingParser<TRoot> : IRankingParser
{
    // Web defaults: camelCase, case-insensitive property matching, numbers may arrive as strings.
    protected static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public abstract string SourceName { get; }

    protected abstract IEnumerable<RankEntry> Map(TRoot root);

    /// <summary>The federation's ranking date, or null when the feed has none. Throw <see cref="ParseException"/> for an unreadable value.</summary>
    protected virtual DateOnly? GetRankingDate(TRoot root) => null;

    public ParsedRanking Parse(string response)
    {
        TRoot root;
        try
        {
            root = JsonSerializer.Deserialize<TRoot>(response, Options)
                   ?? throw new ParseException(SourceName, "response deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new ParseException(SourceName, "response is not the expected JSON shape", ex);
        }

        return new ParsedRanking(Map(root).ToList(), GetRankingDate(root));
    }
}
```

`SportsRankingService/Parsing/HtmlRankingParser.cs`:

```csharp
using HtmlAgilityPack;

namespace SportsRankingService.Parsing;

/// <summary>
/// Base for feeds that return an HTML page. Subclasses give the XPath that selects one node per
/// ranked row, map each row to an entry (or null to skip it), and may override
/// <see cref="GetRankingDate"/> when the page shows the ranking date.
/// </summary>
public abstract class HtmlRankingParser : IRankingParser
{
    public abstract string SourceName { get; }

    protected abstract string RowXPath { get; }

    protected abstract RankEntry? MapRow(HtmlNode row);

    /// <summary>The federation's ranking date, or null when the page has none. Throw <see cref="ParseException"/> for an unreadable value.</summary>
    protected virtual DateOnly? GetRankingDate(HtmlDocument document) => null;

    public ParsedRanking Parse(string response)
    {
        HtmlDocument document = new();
        document.LoadHtml(response);

        HtmlNodeCollection rows = document.DocumentNode.SelectNodes(RowXPath)
            ?? throw new ParseException(SourceName, $"no rows matched {RowXPath}");

        return new ParsedRanking(rows.Select(MapRow).OfType<RankEntry>().ToList(), GetRankingDate(document));
    }
}
```

- [ ] **Step 6: Adapt the runner**

In `SportsRankingService/Services/RankingSourceRunner.cs` change

```csharp
        IReadOnlyList<RankEntry> entries = parser.Parse(response);
        List<SportsRanking> rows = RankingMapper.ToSportsRankings(entries, item);
```

to

```csharp
        ParsedRanking parsed = parser.Parse(response);
        List<SportsRanking> rows = RankingMapper.ToSportsRankings(parsed.Entries, item);
```

- [ ] **Step 7: Adapt the parser tests**

In each of the eleven parser test files, every `_parser.Parse(Fixture.Read("..."))` and `_parser.Parse(json)` now returns `ParsedRanking`. Append `.Entries` wherever the result is used as a list. Concretely:

- `var rows = _parser.Parse(Fixture.Read("X"));` becomes `var rows = _parser.Parse(Fixture.Read("X")).Entries;`
- `_parser.Parse(Fixture.Read("X")).Select(r => r.ISO3)` becomes `_parser.Parse(Fixture.Read("X")).Entries.Select(r => r.ISO3)`
- `Assert.Throws<ParseException>(() => _parser.Parse(json))` stays as is (the throw happens before any member access).

Files: `BwfParserTests.cs` (4 tests), `EspnTennisParserTests.cs`, `FibaParserTests.cs` (2), `FifaV3ParserTests.cs` (3), `FihParserTests.cs` (2), `IccParserTests.cs` (4), `SvnsParserTests.cs` (2), `VolleyballWorldParserTests.cs`, `WbscParserTests.cs` (2), `WorldRugbyParserTests.cs`, `WtaParserTests.cs`.

- [ ] **Step 8: Build and run every test**

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except the 3 skipped. If the benchmark project fails to build it is only because of `RunAsync(...).Count`; that is unchanged in this task (the runner still returns rows), so it should build.

- [ ] **Step 9: Commit**

```bash
git add SportsRankingService/Parsing SportsRankingService/Services/RankingSourceRunner.cs SportsRankingService.Tests/Parsers SportsRankingService.Tests/Parsing
git commit -m "Return a ParsedRanking with an optional ranking date from parsers" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Parsing SportsRankingService/Services/RankingSourceRunner.cs SportsRankingService.Tests/Parsers SportsRankingService.Tests/Parsing
```

---

### Task 4: Federation dates from ICC, WTA, ESPN, WBSC and FIBA

**Files:**
- Modify: `SportsRankingService/Parsers/IccParser.cs`, `WtaParser.cs`, `EspnTennisParser.cs`, `WbscParser.cs`, `FibaParser.cs`
- Test: `SportsRankingService.Tests/Parsers/IccParserTests.cs`, `WtaParserTests.cs`, `EspnTennisParserTests.cs`, `WbscParserTests.cs`, `FibaParserTests.cs`, `FihParserTests.cs`, `VolleyballWorldParserTests.cs`

**Interfaces:**
- Consumes: `GetRankingDate` hooks and `IsoDate.Parse` from Task 3.
- Produces: `ParsedRanking.RankingDate` populated for those five feeds.

- [ ] **Step 1: Add the failing date assertions**

Add one test to each of these files (inside the existing class):

`IccParserTests.cs`:
```csharp
    [Fact]
    public void Carries_the_ICC_rank_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 12), _parser.Parse(Fixture.Read("Icc_Test_Men.json")).RankingDate);
    }
```

`WtaParserTests.cs`:
```csharp
    [Fact]
    public void Carries_the_rankedAt_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), _parser.Parse(Fixture.Read("Wta_Doubles.json")).RankingDate);
    }
```

`EspnTennisParserTests.cs`:
```csharp
    [Fact]
    public void Carries_the_ranking_groups_update_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 10), _parser.Parse(Fixture.Read("Espn_Atp_Singles.json")).RankingDate);
    }
```

`WbscParserTests.cs`:
```csharp
    [Fact]
    public void Carries_the_release_date_of_the_rows()
    {
        Assert.Equal(new DateOnly(2026, 3, 26), _parser.Parse(Fixture.Read("Wbsc_Baseball_Men.json")).RankingDate);
    }
```

`FibaParserTests.cs`:
```csharp
    [Fact]
    public void Carries_the_selected_ranking_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 1), _parser.Parse(Fixture.Read("Fiba_Ranking_Men.html")).RankingDate);
    }
```

`FihParserTests.cs` and `VolleyballWorldParserTests.cs` (these feeds only stamp file-generation time, so they must stay dateless):
```csharp
    [Fact]
    public void Has_no_federation_ranking_date()
    {
        Assert.Null(_parser.Parse(Fixture.Read("Fih_Outdoor_Men.json")).RankingDate);
    }
```
(use `"VolleyballWorld_Men.json"` in the Volleyball file).

- [ ] **Step 2: Run them to confirm the five date tests fail**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~Carries_"`
Expected: 5 failed (actual is null), and the two `Has_no_federation_ranking_date` tests pass.

- [ ] **Step 3: Implement the overrides**

`IccParser.cs`: change the `BatRank` record and add the override.

```csharp
    public sealed record BatRank(List<Entry> Rank, [property: JsonPropertyName("rank_date")] string? RankDate);
```
```csharp
    protected override DateOnly? GetRankingDate(Root root) => IsoDate.Parse(SourceName, root.Data.BatRank.RankDate);
```

`WtaParser.cs`:

```csharp
    public sealed record Entry(short Ranking, Player Player, string? RankedAt);
```
```csharp
    // Every entry carries the same rankedAt; the first is enough. An empty page has no date.
    protected override DateOnly? GetRankingDate(List<Entry> root) =>
        root.Count == 0 ? null : IsoDate.Parse(SourceName, root[0].RankedAt);
```

`EspnTennisParser.cs`:

```csharp
    public sealed record Ranking(List<Rank> Ranks, string? Update);
```
```csharp
    protected override DateOnly? GetRankingDate(Root root) =>
        root.Rankings.FirstOrDefault()?.Update is string update ? IsoDate.Parse(SourceName, update) : null;
```

`WbscParser.cs`:

```csharp
    public sealed record Row(short Position, string Ioc, string? Date);
```
```csharp
    // The API only answers for an exact release date, so every row carries that same date.
    protected override DateOnly? GetRankingDate(Root root) =>
        root.Rankings.Count == 0 ? null : IsoDate.Parse(SourceName, root.Rankings[0].Date);
```

`FibaParser.cs` (add `using HtmlAgilityPack;` is already present):

```csharp
    /// <summary>The page's ranking-date dropdown lists releases newest first with the current one selected.</summary>
    protected override DateOnly? GetRankingDate(HtmlDocument document)
    {
        HtmlNode? option = document.DocumentNode.SelectSingleNode("//select[@name='rankingDatesselect']/option[@selected]")
            ?? document.DocumentNode.SelectSingleNode("//select[@name='rankingDatesselect']/option[1]");

        return option is null ? null : IsoDate.Parse(SourceName, option.GetAttributeValue("value", null));
    }
```

- [ ] **Step 4: Run the whole test project**

Run: `dotnet test SportsRankingService.Tests`
Expected: all pass except 3 skipped.

- [ ] **Step 5: Commit**

```bash
git add SportsRankingService/Parsers SportsRankingService.Tests/Parsers
git commit -m "Read the federation ranking date from ICC, WTA, ESPN, WBSC and FIBA" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Parsers SportsRankingService.Tests/Parsers
```

---

### Task 5: Resolvers return `ResolvedUrl` with the date they discovered

**Files:**
- Create: `SportsRankingService/Services/UrlResolvers/ResolvedUrl.cs`
- Modify: `SportsRankingService/Services/UrlResolvers/IUrlResolver.cs`, `IdentityUrlResolver.cs`, `FifaDateIdResolver.cs`, `WbscReleaseDateResolver.cs`, `SvnsSeriesResolver.cs`
- Modify: `SportsRankingService/Services/RankingSourceRunner.cs`
- Test: `SportsRankingService.Tests/Services/FifaDateIdTests.cs`, `WbscReleaseDateResolverTests.cs`

**Interfaces:**
- Produces: `ResolvedUrl(string Url, DateOnly? RankingDate = null)`; `IUrlResolver.ResolveAsync(RankingItem, CancellationToken) : Task<ResolvedUrl>`; `FifaDateIdResolver.ExtractLatestDate(string html) : SoccerRankDate` (existing `ExtractLatestDateId` stays as a wrapper).

- [ ] **Step 1: Write the failing resolver tests**

Add to `FifaDateIdTests.cs`:

```csharp
    [Fact]
    public void The_ranking_date_is_the_iso_timestamp_of_the_newest_entry_not_the_digits_in_its_id()
    {
        // The men's page's newest entry is id FRS_Male_Football_20260611 with iso 2026-07-20; FIFA displays 20 July.
        SoccerRankDate latest = FifaDateIdResolver.ExtractLatestDate(Fixture.Read("Fifa_WorldRanking_Men.html"));

        Assert.Equal("FRS_Male_Football_20260611", latest.id);
        Assert.Equal(new DateOnly(2026, 7, 20), FifaDateIdResolver.ToRankingDate(latest));
    }

    [Fact]
    public async Task Resolve_formats_the_id_into_the_url_and_returns_the_ranking_date()
    {
        var fetcher = new FakeFetcher(new() { ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Fixture.Read("Fifa_WorldRanking_Men.html") });
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId" };

        ResolvedUrl resolved = await new FifaDateIdResolver(fetcher).ResolveAsync(item, CancellationToken.None);

        Assert.Equal("http://fifa/api?id=FRS_Male_Football_20260611", resolved.Url);
        Assert.Equal(new DateOnly(2026, 7, 20), resolved.RankingDate);
    }

    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(pages.GetValueOrDefault(url));
    }
```

and these usings at the top of that file: `using SportsRankingService.Models;` and `using SportsRankingService.Services;`.

Add to `WbscReleaseDateResolverTests.cs`:

```csharp
    [Fact]
    public async Task Resolve_formats_the_newest_date_into_the_url_and_returns_it()
    {
        var fetcher = new FakeFetcher(new() { ["https://rankings.wbsc.org/"] = Fixture.Read("Wbsc_Rankings.html") });
        var item = new RankingItem { Sport = "Baseball", Gender = "Men", Url = "http://wbsc/api?sportId=baseball-m&date={0}", Source = "Wbsc", UrlResolver = "WbscReleaseDate" };

        ResolvedUrl resolved = await new WbscReleaseDateResolver(fetcher).ResolveAsync(item, CancellationToken.None);

        Assert.Equal("http://wbsc/api?sportId=baseball-m&date=2026-03-26", resolved.Url);
        Assert.Equal(new DateOnly(2026, 3, 26), resolved.RankingDate);
    }

    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(pages.GetValueOrDefault(url));
    }
```

with the same two usings.

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~Resolve_formats|FullyQualifiedName~The_ranking_date_is"`
Expected: build errors (`ResolvedUrl`, `ExtractLatestDate`, `ToRankingDate` missing).

- [ ] **Step 3: Implement `ResolvedUrl` and the resolver changes**

Create `SportsRankingService/Services/UrlResolvers/ResolvedUrl.cs`:

```csharp
namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// The URL to fetch for an item and, when the resolver had to discover a release date to build
/// it, that date. Null means the resolver learned nothing about the ranking date.
/// </summary>
public sealed record ResolvedUrl(string Url, DateOnly? RankingDate = null);
```

`IUrlResolver.cs`: change the method to

```csharp
    Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken);
```

`IdentityUrlResolver.cs`:

```csharp
    public Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken) =>
        Task.FromResult(new ResolvedUrl(item.Url));
```

`SvnsSeriesResolver.cs`: change the signature to `Task<ResolvedUrl>` and the successful return to

```csharp
                return new ResolvedUrl(string.Format(item.Url, id));
```

`FifaDateIdResolver.cs`: replace `ResolveAsync` and the extraction helpers with

```csharp
    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken)
    {
        string page = item.Gender == "Women" ? WomenPage : MenPage;
        string html = (await fetcher.GetStringAsync(page, cancellationToken))
            ?? throw new InvalidOperationException($"FIFA ranking page {page} could not be fetched");

        SoccerRankDate latest = ExtractLatestDate(html);

        return new ResolvedUrl(string.Format(CultureInfo.InvariantCulture, item.Url, latest.id), ToRankingDate(latest));
    }

    /// <summary>
    /// Reads props.pageProps.pageData.ranking.dates (grouped by year) from the __NEXT_DATA__ script
    /// and returns the newest entry by ISO timestamp. Throws if the page has no such data.
    /// </summary>
    internal static SoccerRankDate ExtractLatestDate(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);

        string pageData = document.DocumentNode.SelectSingleNode("//script[@id=\"__NEXT_DATA__\"]").NotNullOrEmpty().InnerText;

        JToken yearGroups = JObject.Parse(pageData)
            .SelectToken("props.pageProps.pageData.ranking.dates")
            .NotNullOrEmpty();

        List<SoccerRankDate> dates = yearGroups
            .SelectMany(year => year["dates"]?.ToObject<List<SoccerRankDate>>() ?? [])
            .ToList()
            .NotNullOrEmpty();

        return dates.MaxBy(d => DateTimeOffset.Parse(d.iso, CultureInfo.InvariantCulture))!;
    }

    internal static string ExtractLatestDateId(string html) => ExtractLatestDate(html).id;

    /// <summary>The date FIFA displays for a release is its iso timestamp's UTC date; the digits in the id are not the ranking date.</summary>
    internal static DateOnly ToRankingDate(SoccerRankDate date) =>
        DateOnly.FromDateTime(DateTimeOffset.Parse(date.iso, CultureInfo.InvariantCulture).UtcDateTime);
```

`WbscReleaseDateResolver.cs`: change `ResolveAsync` to

```csharp
    public async Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken)
    {
        Match sport = SportIdQuery().Match(item.Url);
        if (!sport.Success)
        {
            throw new InvalidOperationException($"WBSC URL has no sportId query parameter: {item.Url}");
        }

        string html = (await fetcher.GetStringAsync(RankingsPage, cancellationToken))
            ?? throw new InvalidOperationException($"WBSC rankings page {RankingsPage} could not be fetched");

        string releaseDate = ExtractLatestReleaseDate(html, sport.Groups[1].Value);

        return new ResolvedUrl(
            string.Format(CultureInfo.InvariantCulture, item.Url, releaseDate),
            DateOnly.ParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
```

`RankingSourceRunner.cs`: change

```csharp
        string url = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await _fetcher.GetStringAsync(url, cancellationToken);
```

to

```csharp
        ResolvedUrl resolved = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await _fetcher.GetStringAsync(resolved.Url, cancellationToken);
```

- [ ] **Step 4: Run all tests**

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped (including the two existing `RankingSourceRunnerTests` that go through the FIFA resolver).

- [ ] **Step 5: Commit**

```bash
git add SportsRankingService/Services SportsRankingService.Tests/Services
git commit -m "Return the discovered release date from the FIFA and WBSC resolvers" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Services SportsRankingService.Tests/Services
```

---

### Task 6: `RankingSnapshot` and `RankingSnapshotBuilder`

**Files:**
- Create: `SportsRankingService/Services/RankingSnapshot.cs`
- Create: `SportsRankingService/Services/RankingSnapshotBuilder.cs`
- Test: `SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs` (create)

**Interfaces:**
- Consumes: `ParsedRanking`, `RankEntry`, `RankingItem`.
- Produces: `RankingSnapshot(string Sport, string? Event, string Gender, DateOnly RankingDate, bool IsFederationDate, IReadOnlyList<RankEntry> Entries)` with `string Describe()`; `RankingSnapshotBuilder.Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today) : RankingSnapshot`.

`RankingMapper` and its tests are not deleted here; Task 8 removes them together with `SportsRanking`.

- [ ] **Step 1: Write the failing tests**

Create `SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs`:

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

    [Fact]
    public void Stamps_sport_event_and_gender_from_the_item()
    {
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(3, "DEU", "Germany")]), null, Today);

        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal([new RankEntry(3, "DEU", "Germany")], snapshot.Entries);
    }

    [Fact]
    public void Entries_come_out_in_position_order()
    {
        RankEntry[] entries = [new(2, "B"), new(1, "A"), new(3, "C")];

        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking(entries), null, Today);

        Assert.Equal(["A", "B", "C"], snapshot.Entries.Select(e => e.ISO3));
    }

    [Fact]
    public void Take_keeps_every_entry_up_to_that_position_including_ties()
    {
        RankingItem item = new() { Sport = "Badminton", Event = "Doubles", Gender = "Men", Url = "http://x", Source = "Bwf", Take = 2 };
        RankEntry[] entries = [new(1, "KOR"), new(1, "KOR"), new(2, "DNK"), new(2, "DNK"), new(3, "CHN"), new(3, "CHN")];

        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, new ParsedRanking(entries), null, Today);

        Assert.Equal(4, snapshot.Entries.Count);
        Assert.All(snapshot.Entries, e => Assert.True(e.Position <= 2));
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
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(Item, new ParsedRanking([new RankEntry(1, "DEU")]), null, Today);

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

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~RankingSnapshotBuilderTests"`
Expected: build error, `RankingSnapshot` missing.

- [ ] **Step 3: Implement**

Create `SportsRankingService/Services/RankingSnapshot.cs`:

```csharp
using SportsRankingService.Parsing;

namespace SportsRankingService.Services;

/// <summary>
/// One feed's ranking as fetched on one run, ready to persist: the feed identity, the ranking
/// date (the federation's when it publishes one, otherwise the scrape date, which
/// <see cref="IsFederationDate"/> distinguishes) and the entries in position order.
/// This is the pipeline's output type; it knows nothing about the database.
/// </summary>
public sealed record RankingSnapshot(
    string Sport,
    string? Event,
    string Gender,
    DateOnly RankingDate,
    bool IsFederationDate,
    IReadOnlyList<RankEntry> Entries)
{
    /// <summary>"Sport Event Gender" for log lines, e.g. "Rugby Sevens Women" or "Basketball Men".</summary>
    public string Describe() =>
        string.Join(" ", new[] { Sport, Event, Gender }.Where(s => !string.IsNullOrEmpty(s)));
}
```

Create `SportsRankingService/Services/RankingSnapshotBuilder.cs`:

```csharp
using SportsRankingService.Models;
using SportsRankingService.Parsing;

namespace SportsRankingService.Services;

/// <summary>Turns parser output into a <see cref="RankingSnapshot"/>: orders, applies Take, stamps the item and decides the date.</summary>
public static class RankingSnapshotBuilder
{
    public static RankingSnapshot Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today)
    {
        // The payload's own date is the most specific; a resolver's date came from the same federation's release list.
        DateOnly? federationDate = parsed.RankingDate ?? resolvedDate;

        IEnumerable<RankEntry> entries = parsed.Entries.OrderBy(e => e.Position);
        if (item.Take is int take)
        {
            // By position, not by row count, so a doubles pair sharing a position is never cut in half.
            entries = entries.Where(e => e.Position <= take);
        }

        return new RankingSnapshot(
            item.Sport,
            item.Event,
            item.Gender,
            federationDate ?? today,
            IsFederationDate: federationDate is not null,
            entries.ToList());
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~RankingSnapshotBuilderTests"`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add SportsRankingService/Services/RankingSnapshot.cs SportsRankingService/Services/RankingSnapshotBuilder.cs SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs
git commit -m "Add RankingSnapshot as the pipeline's persistence-free output" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Services/RankingSnapshot.cs SportsRankingService/Services/RankingSnapshotBuilder.cs SportsRankingService.Tests/Services/RankingSnapshotBuilderTests.cs
```

---

### Task 7: Persistence layer with SQLite-backed repository tests

**Files:**
- Create: `SportsRankingService/Persistence/RankingRelease.cs`
- Create: `SportsRankingService/Persistence/RankingRow.cs`
- Create: `SportsRankingService/Persistence/RankingReleaseConfiguration.cs`
- Create: `SportsRankingService/Persistence/RankingRowConfiguration.cs`
- Create: `SportsRankingService/Persistence/RankingsDbContext.cs`
- Create: `SportsRankingService/Persistence/RankingContentHash.cs`
- Create: `SportsRankingService/Persistence/IRankingRepository.cs`
- Create: `SportsRankingService/Persistence/RankingRepository.cs`
- Modify: `SportsRankingService.Tests/SportsRankingService.Tests.csproj` (add Sqlite and TimeProvider.Testing)
- Test: `SportsRankingService.Tests/Persistence/RankingContentHashTests.cs`, `SportsRankingService.Tests/Persistence/RankingRepositoryTests.cs` (create both)

**Interfaces:**
- Consumes: `RankingSnapshot`, `RankEntry`.
- Produces: `RankingsDbContext(DbContextOptions<RankingsDbContext>)` with `DbSet<RankingRelease> Releases`, `DbSet<RankingRow> Rows`; `enum SaveOutcome { Inserted, Unchanged }`; `IRankingRepository.SaveAsync(RankingSnapshot, CancellationToken) : Task<SaveOutcome>`; `RankingRepository(RankingsDbContext db, TimeProvider clock)`; `RankingContentHash.Compute(RankingSnapshot) : byte[]` (32 bytes).

- [ ] **Step 1: Add test packages**

In `SportsRankingService.Tests/SportsRankingService.Tests.csproj` add to the package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.31" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />
```

- [ ] **Step 2: Write the failing hash tests**

Create `SportsRankingService.Tests/Persistence/RankingContentHashTests.cs`:

```csharp
using SportsRankingService.Parsing;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Persistence;

/// <summary>
/// The hash is a release's identity: same rows and same federation date means the same release.
/// A scrape date is not part of it, so re-scraping an unchanged dateless feed changes nothing.
/// </summary>
public class RankingContentHashTests
{
    private static RankingSnapshot Snapshot(DateOnly date, bool federation, params RankEntry[] entries) =>
        new("Field Hockey", "Outdoor", "Men", date, federation, entries);

    [Fact]
    public void Is_32_bytes_and_deterministic()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands"));
        RankingSnapshot b = Snapshot(new(2026, 9, 12), true, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands"));

        Assert.Equal(32, RankingContentHash.Compute(a).Length);
        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Changes_when_a_row_changes()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new(1, "DEU"), new(2, "NLD"));
        RankingSnapshot b = Snapshot(new(2026, 9, 12), true, new(1, "NLD"), new(2, "DEU"));

        Assert.NotEqual(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Changes_when_the_federation_date_changes()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), true, new(1, "DEU"));
        RankingSnapshot b = Snapshot(new(2026, 9, 19), true, new(1, "DEU"));

        Assert.NotEqual(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Ignores_a_scrape_date()
    {
        RankingSnapshot a = Snapshot(new(2026, 9, 12), false, new(1, "DEU"));
        RankingSnapshot b = Snapshot(new(2026, 9, 19), false, new(1, "DEU"));

        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }

    [Fact]
    public void Ignores_the_feed_identity_because_the_repository_scopes_by_feed()
    {
        RankingSnapshot a = new("Rugby", "Union", "Men", new(2026, 9, 12), false, [new(1, "ZAF")]);
        RankingSnapshot b = new("Rugby", "Union", "Women", new(2026, 9, 12), false, [new(1, "ZAF")]);

        Assert.Equal(RankingContentHash.Compute(a), RankingContentHash.Compute(b));
    }
}
```

- [ ] **Step 3: Write the failing repository tests**

Create `SportsRankingService.Tests/Persistence/RankingRepositoryTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SportsRankingService.Parsing;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Persistence;

/// <summary>
/// Repository behaviour on an in-memory SQLite database created from the EF model (EnsureCreated).
/// This covers the tables and the insert-on-change rule; the CurrentRankings view lives only in
/// the SQL Server migration and is verified against the Docker instance instead.
/// </summary>
public sealed class RankingRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<RankingsDbContext> _options;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));

    public RankingRepositoryTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<RankingsDbContext>().UseSqlite(_connection).Options;

        using RankingsDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot)
    {
        // A fresh context per call, like the updater's per-feed scope.
        using RankingsDbContext db = new(_options);
        return await new RankingRepository(db, _clock).SaveAsync(snapshot, CancellationToken.None);
    }

    private List<RankingRelease> Releases()
    {
        using RankingsDbContext db = new(_options);
        return db.Releases.Include(r => r.Rows.OrderBy(x => x.Ordinal)).OrderBy(r => r.Id).ToList();
    }

    private static RankingSnapshot Hockey(DateOnly date, bool federation, params RankEntry[] entries) =>
        new("Field Hockey", "Outdoor", "Men", date, federation, entries);

    [Fact]
    public async Task First_save_inserts_a_release_with_one_row_per_entry()
    {
        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU", "Germany"), new(2, "NLD", "Netherlands")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        RankingRelease release = Assert.Single(Releases());
        Assert.Equal("Field Hockey", release.Sport);
        Assert.Equal("Outdoor", release.Event);
        Assert.Equal("Men", release.Gender);
        Assert.Equal(new DateOnly(2026, 9, 15), release.RankingDate);
        Assert.False(release.IsFederationDate);
        Assert.Equal(_clock.GetUtcNow(), release.FirstSeenAt);
        Assert.Equal(_clock.GetUtcNow(), release.LastSeenAt);
        Assert.Equal(32, release.ContentHash.Length);
        Assert.Equal([(0, 1, "DEU", "Germany"), (1, 2, "NLD", "Netherlands")], release.Rows.Select(x => (x.Ordinal, (int)x.Position, x.ISO3, x.TeamName)));
    }

    [Fact]
    public async Task An_identical_save_only_bumps_LastSeenAt()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU"), new(2, "NLD")));
        _clock.Advance(TimeSpan.FromDays(7));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 22), false, new(1, "DEU"), new(2, "NLD")));

        Assert.Equal(SaveOutcome.Unchanged, outcome);
        RankingRelease release = Assert.Single(Releases());
        Assert.Equal(new DateOnly(2026, 9, 15), release.RankingDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero), release.FirstSeenAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero), release.LastSeenAt);
    }

    [Fact]
    public async Task Changed_rows_insert_a_second_release_and_keep_the_first()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU"), new(2, "NLD")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 22), false, new(1, "NLD"), new(2, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        List<RankingRelease> releases = Releases();
        Assert.Equal(2, releases.Count);
        Assert.Equal("DEU", releases[0].Rows[0].ISO3);
        Assert.Equal("NLD", releases[1].Rows[0].ISO3);
    }

    [Fact]
    public async Task Same_rows_under_a_new_federation_date_insert_a_release()
    {
        await SaveAsync(Hockey(new(2026, 9, 12), true, new(1, "DEU")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 19), true, new(1, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        Assert.Equal([new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 19)], Releases().Select(r => r.RankingDate));
    }

    [Fact]
    public async Task Reverting_to_older_content_is_still_a_new_release()
    {
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU")));
        await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "NLD")));

        SaveOutcome outcome = await SaveAsync(Hockey(new(2026, 9, 15), false, new(1, "DEU")));

        Assert.Equal(SaveOutcome.Inserted, outcome);
        Assert.Equal(3, Releases().Count);
    }

    [Fact]
    public async Task Two_entries_at_the_same_position_are_both_stored()
    {
        RankingSnapshot doubles = new("Badminton", "Doubles", "Men", new(2026, 9, 15), false, [new(1, "KOR", "Kim"), new(1, "KOR", "Seo")]);

        await SaveAsync(doubles);

        RankingRelease release = Assert.Single(Releases());
        Assert.Equal(["Kim", "Seo"], release.Rows.Select(x => x.TeamName));
        Assert.All(release.Rows, x => Assert.Equal(1, x.Position));
    }

    [Fact]
    public async Task Feeds_are_independent_including_a_null_event()
    {
        RankingSnapshot basketballMen = new("Basketball", null, "Men", new(2026, 9, 1), true, [new(1, "USA")]);
        RankingSnapshot basketballWomen = new("Basketball", null, "Women", new(2026, 9, 1), true, [new(1, "USA")]);

        Assert.Equal(SaveOutcome.Inserted, await SaveAsync(basketballMen));
        Assert.Equal(SaveOutcome.Inserted, await SaveAsync(basketballWomen));
        Assert.Equal(SaveOutcome.Unchanged, await SaveAsync(basketballMen));
        Assert.Equal(SaveOutcome.Unchanged, await SaveAsync(basketballWomen));
        Assert.Equal(2, Releases().Count);
    }
}
```

- [ ] **Step 4: Run them to confirm they fail**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~Persistence"`
Expected: build errors, `SportsRankingService.Persistence` namespace missing.

- [ ] **Step 5: Implement the entities and configurations**

`SportsRankingService/Persistence/RankingRelease.cs`:

```csharp
namespace SportsRankingService.Persistence;

/// <summary>
/// One published ranking table for one feed. A release is immutable once written; a re-scrape that
/// produces the same content only moves <see cref="LastSeenAt"/>. All releases of a feed are its
/// history, and the one with the greatest <see cref="Id"/> is current.
/// </summary>
public class RankingRelease
{
    public int Id { get; set; }

    public required string Sport { get; set; }

    public string? Event { get; set; }

    public required string Gender { get; set; }

    /// <summary>The federation's ranking date when <see cref="IsFederationDate"/>, otherwise the scrape date.</summary>
    public DateOnly RankingDate { get; set; }

    public bool IsFederationDate { get; set; }

    /// <summary>SHA-256 of the rows plus the federation date; see RankingContentHash.</summary>
    public required byte[] ContentHash { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public List<RankingRow> Rows { get; set; } = [];
}
```

`SportsRankingService/Persistence/RankingRow.cs`:

```csharp
namespace SportsRankingService.Persistence;

/// <summary>One entry of a <see cref="RankingRelease"/>. Ordinal is the feed order; positions can repeat (doubles partners).</summary>
public class RankingRow
{
    public int ReleaseId { get; set; }

    public int Ordinal { get; set; }

    public short Position { get; set; }

    /// <summary>ISO 3166-1 alpha-3, except FIFA's home nations (ENG, SCO, WAL, NIR) and West Indies (WI).</summary>
    public required string ISO3 { get; set; }

    public string? TeamName { get; set; }

    public RankingRelease Release { get; set; } = null!;
}
```

`SportsRankingService/Persistence/RankingReleaseConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SportsRankingService.Persistence;

public sealed class RankingReleaseConfiguration : IEntityTypeConfiguration<RankingRelease>
{
    public void Configure(EntityTypeBuilder<RankingRelease> release)
    {
        release.ToTable("RankingReleases");
        release.HasKey(r => r.Id);
        release.Property(r => r.Sport).HasMaxLength(50);
        release.Property(r => r.Event).HasMaxLength(50);
        release.Property(r => r.Gender).HasMaxLength(10);
        release.Property(r => r.ContentHash).HasMaxLength(32).IsFixedLength();

        // "Newest release for a feed" is the lookup the repository and the CurrentRankings view both do.
        release.HasIndex(r => new { r.Sport, r.Event, r.Gender }).HasDatabaseName("IX_RankingReleases_Feed");

        release.HasMany(r => r.Rows)
            .WithOne(x => x.Release)
            .HasForeignKey(x => x.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`SportsRankingService/Persistence/RankingRowConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SportsRankingService.Persistence;

public sealed class RankingRowConfiguration : IEntityTypeConfiguration<RankingRow>
{
    public void Configure(EntityTypeBuilder<RankingRow> row)
    {
        row.ToTable("RankingRows");
        row.HasKey(x => new { x.ReleaseId, x.Ordinal });
        // varchar(3), not char(3): West Indies is "WI".
        row.Property(x => x.ISO3).HasMaxLength(3).IsUnicode(false);
        row.Property(x => x.TeamName).HasMaxLength(100);
    }
}
```

`SportsRankingService/Persistence/RankingsDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace SportsRankingService.Persistence;

public sealed class RankingsDbContext(DbContextOptions<RankingsDbContext> options) : DbContext(options)
{
    public DbSet<RankingRelease> Releases => Set<RankingRelease>();

    public DbSet<RankingRow> Rows => Set<RankingRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RankingsDbContext).Assembly);
}
```

- [ ] **Step 6: Implement the hash and the repository**

`SportsRankingService/Persistence/RankingContentHash.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SportsRankingService.Parsing;
using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

/// <summary>
/// Identity of a release's content: SHA-256 over a header line (the ranking date when it is the
/// federation's, otherwise empty) and one "position\tISO3\tteam name" line per entry in order.
/// The feed identity is not included because the repository compares within one feed only.
/// </summary>
public static class RankingContentHash
{
    public static byte[] Compute(RankingSnapshot snapshot)
    {
        StringBuilder text = new();

        text.Append(snapshot.IsFederationDate ? snapshot.RankingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "").Append('\n');

        foreach (RankEntry entry in snapshot.Entries)
        {
            text.Append(entry.Position.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(entry.ISO3).Append('\t')
                .Append(entry.TeamName).Append('\n');
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
```

`SportsRankingService/Persistence/IRankingRepository.cs`:

```csharp
using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

public enum SaveOutcome
{
    /// <summary>The content differed from the feed's newest release (or there was none), so a new release was written.</summary>
    Inserted,

    /// <summary>Same content as the feed's newest release; only its LastSeenAt moved.</summary>
    Unchanged,
}

public interface IRankingRepository
{
    Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken);
}
```

`SportsRankingService/Persistence/RankingRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Services;

namespace SportsRankingService.Persistence;

/// <summary>Insert-on-change: compares the snapshot's hash with the feed's newest release and only writes when it differs.</summary>
public sealed class RankingRepository(RankingsDbContext db, TimeProvider clock) : IRankingRepository
{
    public async Task<SaveOutcome> SaveAsync(RankingSnapshot snapshot, CancellationToken cancellationToken)
    {
        byte[] hash = RankingContentHash.Compute(snapshot);
        DateTimeOffset now = clock.GetUtcNow();

        // Greatest Id is newest; identity order is insertion order and it works on every provider.
        RankingRelease? newest = await db.Releases
            .Where(r => r.Sport == snapshot.Sport && r.Event == snapshot.Event && r.Gender == snapshot.Gender)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is not null && newest.ContentHash.AsSpan().SequenceEqual(hash))
        {
            newest.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Unchanged;
        }

        RankingRelease release = new()
        {
            Sport = snapshot.Sport,
            Event = snapshot.Event,
            Gender = snapshot.Gender,
            RankingDate = snapshot.RankingDate,
            IsFederationDate = snapshot.IsFederationDate,
            ContentHash = hash,
            FirstSeenAt = now,
            LastSeenAt = now,
            Rows = snapshot.Entries
                .Select((entry, ordinal) => new RankingRow
                {
                    Ordinal = ordinal,
                    Position = entry.Position,
                    ISO3 = entry.ISO3,
                    TeamName = entry.TeamName,
                })
                .ToList(),
        };

        db.Releases.Add(release);
        await db.SaveChangesAsync(cancellationToken);
        return SaveOutcome.Inserted;
    }
}
```

- [ ] **Step 7: Run the persistence tests, then everything**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~Persistence"`
Expected: 12 passed.

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped.

- [ ] **Step 8: Commit**

```bash
git add SportsRankingService/Persistence SportsRankingService.Tests/Persistence SportsRankingService.Tests/SportsRankingService.Tests.csproj
git commit -m "Add the release-based persistence layer with insert-on-change" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Persistence SportsRankingService.Tests/Persistence SportsRankingService.Tests/SportsRankingService.Tests.csproj
```

---

### Task 8: Wire the pipeline to the repository and remove the old EF model

**Files:**
- Modify: `SportsRankingService/Services/RankingSourceRunner.cs`
- Modify: `SportsRankingService/Services/IRankingUpdater.cs`
- Modify: `SportsRankingService/Services/RankingUpdater.cs`
- Modify: `SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs`
- Modify: `SportsRankingService/Program.cs`
- Modify: `WebScrapingBenchmarks/Benchmarks/ScrapeServiceBenchmark.cs`
- Modify: `SportsRankingService.Tests/Services/RankingSourceRunnerTests.cs`
- Delete: `SportsRankingService/Services/RankingMapper.cs`, `SportsRankingService.Tests/Services/RankingMapperTests.cs`, `SportsRankingService/DbContext/WorldRankGuesserContext.cs`, `SportsRankingService/Models/SportsRanking.Model.cs`, `SportsRankingService/Models/SportsRanking.Builder.cs`, `SportsRankingService/Models/SportsRankings_History.cs`, `SportsRankingService/Models/IRanking.cs`, `SportsRankingService/Repository/IWorldRankRepository.cs`, `SportsRankingService/Repository/WorldRankRepository.cs`, `SportsRankingService/efpt.config.json`, `SportsRankingService/CodeTemplates/EFCore/DbContext.t4`, `SportsRankingService/CodeTemplates/EFCore/EntityType.t4`

**Interfaces:**
- Consumes: `RankingSnapshotBuilder`, `IRankingRepository`, `SaveOutcome`, `RankingsDbContext`.
- Produces: `RankingSourceRunner(IHttpFetcher, IEnumerable<IRankingParser>, IEnumerable<IUrlResolver>, TimeProvider, ILogger<RankingSourceRunner>)` with `RunAsync(RankingItem, CancellationToken) : Task<RankingSnapshot?>`; `UpdateSummary(int Feeds, int Inserted, int Unchanged, int Failed)`; `IRankingUpdater.UpdateAllAsync(CancellationToken) : Task<UpdateSummary>`.

- [ ] **Step 1: Update the runner tests first**

Rewrite `SportsRankingService.Tests/Services/RankingSourceRunnerTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.Parsing;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// End-to-end for one configured item without the network: resolve the URL, fetch, parse, stamp, date.
/// </summary>
public class RankingSourceRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public List<string> Requested { get; } = [];

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(pages.GetValueOrDefault(url));
        }
    }

    private static RankingSourceRunner Runner(FakeFetcher fetcher) =>
        new(fetcher,
            [new FihParser(), new FifaV3Parser()],
            [new IdentityUrlResolver(), new FifaDateIdResolver(fetcher)],
            new FakeTimeProvider(Now),
            NullLogger<RankingSourceRunner>.Instance);

    [Fact]
    public async Task Static_feed_is_fetched_parsed_and_stamped()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") });
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal(104, snapshot.Entries.Count);
        Assert.Equal("DEU", snapshot.Entries.Single(e => e.Position == 1).ISO3);
    }

    [Fact]
    public async Task A_dateless_feed_gets_todays_date_flagged_as_not_the_federations()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") });
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(new DateOnly(2026, 9, 15), snapshot.RankingDate);
        Assert.False(snapshot.IsFederationDate);
    }

    [Fact]
    public async Task Resolver_runs_its_preliminary_request_and_supplies_the_ranking_date()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Fixture.Read("Fifa_WorldRanking_Men.html"),
            ["http://fifa/api?id=FRS_Male_Football_20260611"] = Fixture.Read("Fifa_V3_Men_FRS_20260611.json"),
        });
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(211, snapshot.Entries.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
        Assert.Equal(["https://inside.fifa.com/fifa-rankings/world-ranking/men", "http://fifa/api?id=FRS_Male_Football_20260611"], fetcher.Requested);
    }

    [Fact]
    public async Task Failed_fetch_yields_null_and_does_not_throw()
    {
        var fetcher = new FakeFetcher([]);
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://down", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task Unknown_source_is_a_configuration_error()
    {
        var item = new RankingItem { Sport = "Chess", Gender = "Men", Url = "http://x", Source = "Fide" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(new FakeFetcher([])).RunAsync(item, CancellationToken.None));

        Assert.Contains("Fide", ex.Message);
    }

    [Fact]
    public async Task Malformed_response_surfaces_as_a_parse_exception()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih"] = "<html>maintenance</html>" });
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://fih", Source = "Fih" };

        await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run them to confirm they fail**

Run: `dotnet test SportsRankingService.Tests --filter "FullyQualifiedName~RankingSourceRunnerTests"`
Expected: build errors (constructor arity, return type).

- [ ] **Step 3: Rewrite the runner**

`SportsRankingService/Services/RankingSourceRunner.cs`:

```csharp
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

/// <summary>
/// Runs one configured feed end to end: resolve the URL, fetch it, parse it, build the snapshot.
/// A failed fetch yields null (already logged by the fetcher). A malformed response throws
/// <see cref="ParseException"/>; an unknown Source or UrlResolver name throws
/// <see cref="InvalidOperationException"/> because that is a configuration error.
/// </summary>
public sealed class RankingSourceRunner
{
    private readonly IHttpFetcher _fetcher;
    private readonly IReadOnlyDictionary<string, IRankingParser> _parsers;
    private readonly IReadOnlyDictionary<string, IUrlResolver> _resolvers;
    private readonly TimeProvider _clock;
    private readonly ILogger<RankingSourceRunner> _logger;

    public RankingSourceRunner(
        IHttpFetcher fetcher,
        IEnumerable<IRankingParser> parsers,
        IEnumerable<IUrlResolver> resolvers,
        TimeProvider clock,
        ILogger<RankingSourceRunner> logger)
    {
        _fetcher = fetcher;
        _parsers = parsers.ToDictionary(p => p.SourceName, StringComparer.OrdinalIgnoreCase);
        _resolvers = resolvers.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _clock = clock;
        _logger = logger;
    }

    public async Task<RankingSnapshot?> RunAsync(RankingItem item, CancellationToken cancellationToken)
    {
        if (!_parsers.TryGetValue(item.Source, out IRankingParser? parser))
        {
            throw new InvalidOperationException($"No parser registered for Source '{item.Source}' ({Describe(item)}). Known: {string.Join(", ", _parsers.Keys)}");
        }

        if (!_resolvers.TryGetValue(item.UrlResolver, out IUrlResolver? resolver))
        {
            throw new InvalidOperationException($"No URL resolver registered for '{item.UrlResolver}' ({Describe(item)}). Known: {string.Join(", ", _resolvers.Keys)}");
        }

        ResolvedUrl resolved = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await _fetcher.GetStringAsync(resolved.Url, cancellationToken);

        if (response is null)
        {
            return null;
        }

        ParsedRanking parsed = parser.Parse(response);
        DateOnly today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, parsed, resolved.RankingDate, today);

        _logger.LogInformation("Parsed {Count} rows for {Item}, ranking date {Date} ({DateSource})",
            snapshot.Entries.Count, snapshot.Describe(), snapshot.RankingDate, snapshot.IsFederationDate ? "federation" : "scrape date");

        return snapshot;
    }

    internal static string Describe(RankingItem item) =>
        string.Join(" ", new[] { item.Sport, item.Event, item.Gender }.Where(s => !string.IsNullOrEmpty(s)));
}
```

- [ ] **Step 4: Rewrite the updater and its interface**

`SportsRankingService/Services/IRankingUpdater.cs`:

```csharp
namespace SportsRankingService.Services;

/// <summary>Counts for one run. A feed that yielded nothing or threw is Failed; the others are Inserted or Unchanged.</summary>
public sealed record UpdateSummary(int Feeds, int Inserted, int Unchanged, int Failed);

public interface IRankingUpdater
{
    /// <summary>Fetches every enabled feed and saves its snapshot. One failing feed does not affect the others.</summary>
    Task<UpdateSummary> UpdateAllAsync(CancellationToken cancellationToken);
}
```

`SportsRankingService/Services/RankingUpdater.cs`:

```csharp
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Models;
using SportsRankingService.Persistence;

namespace SportsRankingService.Services;

public sealed class RankingUpdater(
    RankingSourceRunner runner,
    IOptionsMonitor<RankingSourcesOptions> sources,
    IServiceScopeFactory scopeFactory,
    ILogger<RankingUpdater> logger) : IRankingUpdater
{
    public async Task<UpdateSummary> UpdateAllAsync(CancellationToken cancellationToken)
    {
        List<RankingItem> items = sources.CurrentValue.Rankings.Where(i => i.Enabled).ToList();

        if (items.Count == 0)
        {
            logger.LogWarning("No enabled ranking items configured; check serviceconfig.json");
            return new UpdateSummary(0, 0, 0, 0);
        }

        SaveOutcome?[] outcomes = await Task.WhenAll(items.Select(item => UpdateItemAsync(item, cancellationToken)));

        UpdateSummary summary = new(
            Feeds: items.Count,
            Inserted: outcomes.Count(o => o == SaveOutcome.Inserted),
            Unchanged: outcomes.Count(o => o == SaveOutcome.Unchanged),
            Failed: outcomes.Count(o => o is null));

        logger.LogInformation("{Feeds} feeds: {Inserted} new releases, {Unchanged} unchanged, {Failed} failed",
            summary.Feeds, summary.Inserted, summary.Unchanged, summary.Failed);

        return summary;
    }

    /// <returns>The save outcome, or null when the feed produced nothing or threw.</returns>
    private async Task<SaveOutcome?> UpdateItemAsync(RankingItem item, CancellationToken cancellationToken)
    {
        string name = RankingSourceRunner.Describe(item);

        try
        {
            RankingSnapshot? snapshot = await runner.RunAsync(item, cancellationToken);

            if (snapshot is null || snapshot.Entries.Count == 0)
            {
                logger.LogWarning("No rows for {Item}; nothing saved", name);
                return null;
            }

            // The updater is transient but the DbContext is scoped, so each feed gets its own scope.
            using IServiceScope scope = scopeFactory.CreateScope();
            IRankingRepository repository = scope.ServiceProvider.GetRequiredService<IRankingRepository>();
            SaveOutcome outcome = await repository.SaveAsync(snapshot, cancellationToken);

            logger.LogInformation("{Item}: {Outcome} ({Count} rows, ranking date {Date})", name, outcome, snapshot.Entries.Count, snapshot.RankingDate);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feed {Item} failed; other feeds are unaffected", name);
            return null;
        }
    }
}
```

- [ ] **Step 5: Register the persistence services and delete the old model**

In `SportsRankingService/Services/RankingPipelineServiceCollectionExtensions.cs` add, just before `services.AddSingleton<RankingSourceRunner>();`:

```csharp
        services.TryAddSingleton(TimeProvider.System);
```

and the using `using Microsoft.Extensions.DependencyInjection.Extensions;` at the top. (`TryAdd` so a test host can register a fake first.)

In `SportsRankingService/Program.cs` replace

```csharp
        services.AddDbContext<WorldRankGuesserContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection")));
```

with

```csharp
        services.AddDbContext<RankingsDbContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection")));
        services.AddScoped<IRankingRepository, RankingRepository>();
```

and replace `using SportsRankingService.RankingsDb;` with `using SportsRankingService.Persistence;`.

`Worker.cs` still calls `UpdateAllAsync` and ignores the result; that compiles unchanged (the returned summary is simply not used) and is removed in Task 9.

Delete the superseded EF model and the mapper:

```bash
git rm SportsRankingService/Services/RankingMapper.cs SportsRankingService.Tests/Services/RankingMapperTests.cs SportsRankingService/DbContext/WorldRankGuesserContext.cs SportsRankingService/Models/SportsRanking.Model.cs SportsRankingService/Models/SportsRanking.Builder.cs SportsRankingService/Models/SportsRankings_History.cs SportsRankingService/Models/IRanking.cs SportsRankingService/Repository/IWorldRankRepository.cs SportsRankingService/Repository/WorldRankRepository.cs SportsRankingService/efpt.config.json SportsRankingService/CodeTemplates/EFCore/DbContext.t4 SportsRankingService/CodeTemplates/EFCore/EntityType.t4
```

If the permission mode refuses the deletion, blank each file to a single line `// Superseded; delete this file.` (a `.t4` or `.json` file can hold that line too, since nothing reads them) and list them for the user in the final report.

- [ ] **Step 6: Adapt the benchmark**

In `WebScrapingBenchmarks/Benchmarks/ScrapeServiceBenchmark.cs` change the loop body to

```csharp
                rows += (await _runner.RunAsync(item, CancellationToken.None))?.Entries.Count ?? 0;
```

- [ ] **Step 7: Build and test**

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped. Confirm no EF reference leaks into the pipeline:

Run: `grep -rl "EntityFrameworkCore" SportsRankingService/Parsing SportsRankingService/Parsers SportsRankingService/Services SportsRankingService/Models SportsRankingService/Configuration`
Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add -A SportsRankingService/Services SportsRankingService/Program.cs SportsRankingService/DbContext SportsRankingService/Models SportsRankingService/Repository SportsRankingService/efpt.config.json SportsRankingService/CodeTemplates WebScrapingBenchmarks/Benchmarks/ScrapeServiceBenchmark.cs SportsRankingService.Tests/Services
git commit -m "Save snapshots through the release repository and drop the old EF model" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Services SportsRankingService/Program.cs SportsRankingService/DbContext SportsRankingService/Models SportsRankingService/Repository SportsRankingService/efpt.config.json SportsRankingService/CodeTemplates WebScrapingBenchmarks/Benchmarks/ScrapeServiceBenchmark.cs SportsRankingService.Tests/Services
```

---

### Task 9: Console entry point that runs once and exits

**Files:**
- Modify: `SportsRankingService/SportsRankingService.csproj`
- Modify: `SportsRankingService/Program.cs`
- Modify: `SportsRankingService/appsettings.json` (remove `Worker`)
- Delete: `SportsRankingService/Worker.cs`, `SportsRankingService/Configuration/WorkerOptions.cs`

**Interfaces:**
- Consumes: `IRankingUpdater.UpdateAllAsync : Task<UpdateSummary>`, `RankingsDbContext`, `IRankingRepository`.
- Produces: process exit code 0 when every feed saved, 1 otherwise.

- [ ] **Step 1: Switch the project to a plain console app**

Replace `SportsRankingService/SportsRankingService.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>dotnet-SportsRankingService-e2f860ef-eaf3-46f9-859c-0b7d47b88df1</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <!-- The Worker SDK used to add these global usings; the generic host is still the DI/config/logging container. -->
    <Using Include="Microsoft.Extensions.Configuration" />
    <Using Include="Microsoft.Extensions.DependencyInjection" />
    <Using Include="Microsoft.Extensions.Hosting" />
    <Using Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <!-- Loaded from the content root (AppContext.BaseDirectory) at startup, so they must sit next to the binaries. -->
    <None Update="appsettings.json;appsettings.Development.json;serviceconfig.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="SportsRankingService.Tests" />
    <InternalsVisibleTo Include="SportsRankingBenchmarks" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.54" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.31" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.31">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Rewrite `Program.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Configuration;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

// A run-once console app: an external scheduler (Task Scheduler, cron) runs it weekly.
// The generic host is kept only as the configuration / logging / DI container.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // appsettings.json and serviceconfig.json sit next to the binaries, so the working directory does not matter.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Configuration.AddJsonFile("serviceconfig.json", optional: false);

builder.Services.Configure<RankingSourcesOptions>(builder.Configuration);
builder.Services.AddDbContext<RankingsDbContext>(
    options => options.UseSqlServer(builder.Configuration.GetConnectionString("WorldRankGuesserConnection")));
builder.Services.AddScoped<IRankingRepository, RankingRepository>();
builder.Services.AddRankingPipeline();

builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Build() stays outside the try: `dotnet ef` intercepts it at design time by throwing HostAbortedException.
using IHost host = builder.Build();
ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SportsRankingService");

using CancellationTokenSource shutdown = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

try
{
    using IServiceScope scope = host.Services.CreateScope();
    IRankingUpdater updater = scope.ServiceProvider.GetRequiredService<IRankingUpdater>();

    UpdateSummary summary = await updater.UpdateAllAsync(shutdown.Token);

    return summary.Failed > 0 ? 1 : 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    logger.LogWarning("Ranking update cancelled");
    return 1;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Ranking update run failed");
    return 1;
}
```

- [ ] **Step 3: Remove the worker loop and its options**

```bash
git rm SportsRankingService/Worker.cs SportsRankingService/Configuration/WorkerOptions.cs
```

(Blank them with `// Superseded; delete this file.` if deletion is refused.)

Remove the `Worker` section from `SportsRankingService/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "WorldRankGuesserConnection": "Server=localhost,1433;Database=WorldRankGuesser;User Id=sa;Password=Rankings_Dev1!;TrustServerCertificate=True"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

- [ ] **Step 4: Build, test, and run against the container (no schema yet, so expect a clean failure)**

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped.

Run: `dotnet run --project SportsRankingService; echo "exit $LASTEXITCODE"` (PowerShell) or `dotnet run --project SportsRankingService; echo "exit $?"` (bash)
Expected: feeds are fetched and parsed, every save fails with a SQL error about the missing database or table (`Cannot open database "WorldRankGuesser"` or `Invalid object name 'RankingReleases'`), the summary line reports all feeds failed, and the exit code is 1. This proves the process runs once and exits with a meaningful code; Task 10 creates the schema.

- [ ] **Step 5: Commit**

```bash
git add -A SportsRankingService/SportsRankingService.csproj SportsRankingService/Program.cs SportsRankingService/appsettings.json SportsRankingService/Worker.cs SportsRankingService/Configuration
git commit -m "Run once as a console app and exit with a status code" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/SportsRankingService.csproj SportsRankingService/Program.cs SportsRankingService/appsettings.json SportsRankingService/Worker.cs SportsRankingService/Configuration
```

---

### Task 10: Initial migration with the CurrentRankings view, verified live

**Files:**
- Create: `SportsRankingService/Persistence/Migrations/<timestamp>_InitialSchema.cs` (generated, then edited)
- Create: `SportsRankingService/Persistence/Migrations/<timestamp>_InitialSchema.Designer.cs` (generated)
- Create: `SportsRankingService/Persistence/Migrations/RankingsDbContextModelSnapshot.cs` (generated)
- Possibly create: `SportsRankingService/Persistence/RankingsDbContextFactory.cs` (only if design-time discovery fails)

**Interfaces:**
- Produces: tables `RankingReleases`, `RankingRows`, view `dbo.CurrentRankings`.

- [ ] **Step 1: Generate the migration**

Run: `dotnet ef migrations add InitialSchema --project SportsRankingService --startup-project SportsRankingService --output-dir Persistence/Migrations`
Expected: `Done.` and three files under `SportsRankingService/Persistence/Migrations/`.

If instead it fails with `Unable to create a 'DbContext' of type 'RankingsDbContext'`, add `SportsRankingService/Persistence/RankingsDbContextFactory.cs` and rerun:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SportsRankingService.Persistence;

/// <summary>Design-time context for `dotnet ef`; reads the same appsettings.json the app uses.</summary>
public sealed class RankingsDbContextFactory : IDesignTimeDbContextFactory<RankingsDbContext>
{
    public RankingsDbContext CreateDbContext(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        DbContextOptions<RankingsDbContext> options = new DbContextOptionsBuilder<RankingsDbContext>()
            .UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection"))
            .Options;

        return new RankingsDbContext(options);
    }
}
```

- [ ] **Step 2: Check the generated `Up` matches the model**

Open `<timestamp>_InitialSchema.cs` and confirm: `RankingReleases` with `Id int identity`, `Sport nvarchar(50)`, `Event nvarchar(50) nullable`, `Gender nvarchar(10)`, `RankingDate date`, `IsFederationDate bit`, `ContentHash binary(32)`, `FirstSeenAt datetimeoffset`, `LastSeenAt datetimeoffset`; `RankingRows` with composite key `(ReleaseId, Ordinal)`, `Position smallint`, `ISO3 varchar(3)`, `TeamName nvarchar(100) nullable`, FK cascade; index `IX_RankingReleases_Feed` on `(Sport, Event, Gender)`. If a column type differs (for example `char(3)` or `varbinary`), fix the configuration in Task 7's files, delete the migration with `dotnet ef migrations remove --project SportsRankingService --startup-project SportsRankingService`, and regenerate.

- [ ] **Step 3: Add the view to the migration**

At the end of `Up(MigrationBuilder migrationBuilder)` append:

```csharp
            // Read model for consumers: the newest release per feed, one row per entry.
            migrationBuilder.Sql("""
                CREATE VIEW dbo.CurrentRankings AS
                SELECT r.Sport, r.Event, r.Gender, r.RankingDate, r.IsFederationDate, x.Position, x.ISO3, x.TeamName
                FROM dbo.RankingReleases r
                JOIN dbo.RankingRows x ON x.ReleaseId = r.Id
                WHERE r.Id = (
                    SELECT MAX(n.Id)
                    FROM dbo.RankingReleases n
                    WHERE n.Sport = r.Sport
                      AND n.Gender = r.Gender
                      AND (n.Event = r.Event OR (n.Event IS NULL AND r.Event IS NULL)));
                """);
```

At the start of `Down(MigrationBuilder migrationBuilder)` insert:

```csharp
            migrationBuilder.Sql("DROP VIEW dbo.CurrentRankings;");
```

- [ ] **Step 4: Apply it to the container**

Run: `docker compose up -d --wait` (no-op if already running)
Run: `dotnet ef database update --project SportsRankingService --startup-project SportsRankingService`
Expected: `Applying migration '<timestamp>_InitialSchema'.` then `Done.`

Run: `docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -d WorldRankGuesser -Q "SELECT name FROM sys.tables; SELECT name FROM sys.views"`
Expected: tables `__EFMigrationsHistory`, `RankingReleases`, `RankingRows`; view `CurrentRankings`.

- [ ] **Step 5: Run the app twice and check insert-on-change end to end**

Run: `dotnet run --project SportsRankingService; echo "exit $LASTEXITCODE"`
Expected: a summary line like `29 feeds: 29 new releases, 0 unchanged, 0 failed` and exit 0. (A feed that is down that day shows as failed and exit 1; that is correct behaviour, note it in the report.)

Run: `docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Rankings_Dev1!' -C -d WorldRankGuesser -Q "SELECT COUNT(*) AS Releases FROM RankingReleases; SELECT COUNT(*) AS CurrentRows FROM CurrentRankings; SELECT Sport, Event, Gender, RankingDate, IsFederationDate FROM RankingReleases ORDER BY Sport, Event, Gender"`
Expected: release count equals the number of feeds that succeeded; `CurrentRows` roughly 2,500; ICC, WTA, ESPN, WBSC, FIBA, FIFA rows show `IsFederationDate = 1` with dates in the past; FIH, Volleyball, World Rugby, SVNS show `0` with today's date.

Run the app a second time: `dotnet run --project SportsRankingService`
Expected: summary `... 0 new releases, N unchanged, ...`; the release count query returns the same number as before, and `SELECT MIN(LastSeenAt), MAX(LastSeenAt) FROM RankingReleases` shows `LastSeenAt` later than `FirstSeenAt` (query `SELECT COUNT(*) FROM RankingReleases WHERE LastSeenAt > FirstSeenAt`, expect N).

- [ ] **Step 6: Run the full test suite and commit**

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped.

```bash
git add SportsRankingService/Persistence
git commit -m "Add the initial migration with the CurrentRankings view" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>" -- SportsRankingService/Persistence
```

---

### Task 11: Delete superseded files and update the docs

**Files:**
- Delete: `SportsRankingService/Utilities/LogHelper.cs`, `SportsRankingService/serviceconfig_test.json`, `SportsRankingService.Tests/Parsers/BadmintonParserTests.cs`, `BaseballParserTests.cs`, `BasketballParserTests.cs`, `CricketParserTests.cs`, `GymnasticsParserTests.cs`, `HockeyParserTests.cs`, `RugbyParserTests.cs`, `SoccerParserTests.cs`, `TennisParserTests.cs`, `VolleyballParserTests.cs`
- Modify: `CLAUDE.md`, `README.md`
- Commit: the deletions that were already staged before this work (`Enums/`, `Factories/`, old `Parsers/*Parser.cs`, `Models/WorldRankInfo.cs`, `Services/IScrapeService.cs`, `Services/WorldRankService.cs`, `Utilities/ConfigHelper.cs`, `Utilities/ServiceConfigJsonConverter.cs`) and the small `CountryUtil.cs` rename.

- [ ] **Step 1: Delete the leftovers**

```bash
git rm SportsRankingService/Utilities/LogHelper.cs SportsRankingService/serviceconfig_test.json SportsRankingService.Tests/Parsers/BadmintonParserTests.cs SportsRankingService.Tests/Parsers/BaseballParserTests.cs SportsRankingService.Tests/Parsers/BasketballParserTests.cs SportsRankingService.Tests/Parsers/CricketParserTests.cs SportsRankingService.Tests/Parsers/GymnasticsParserTests.cs SportsRankingService.Tests/Parsers/HockeyParserTests.cs SportsRankingService.Tests/Parsers/RugbyParserTests.cs SportsRankingService.Tests/Parsers/SoccerParserTests.cs SportsRankingService.Tests/Parsers/TennisParserTests.cs SportsRankingService.Tests/Parsers/VolleyballParserTests.cs
```

(If refused, leave them and list them for the user.)

Run: `dotnet build SportsRankingService.sln` then `dotnet test SportsRankingService.Tests`
Expected: 0 errors; all pass except 3 skipped.

- [ ] **Step 2: Rewrite `README.md`**

````markdown
# SportsRankingService

Scrapes world team rankings (FIFA, ICC, World Rugby, FIH, WBSC, FIBA, tennis, volleyball, ...) from
public federation feeds and stores them in SQL Server as immutable *releases*: a new release is
written only when a feed's content or federation ranking date changes, so the database holds both
the current table per feed and its history.

## Setup

```powershell
docker compose up -d --wait                              # SQL Server 2022 on localhost,1433 (sa / Rankings_Dev1!)
dotnet tool restore                                      # dotnet-ef
dotnet ef database update --project SportsRankingService --startup-project SportsRankingService # create the database and schema
dotnet run --project SportsRankingService                # fetch every enabled feed once and exit
dotnet test SportsRankingService.Tests                   # fixture-based tests, no network or database
```

The process exits 0 when every enabled feed was saved and 1 when any feed failed, so a scheduler's
"last run result" is meaningful.

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

`CurrentRankings` is a view with one row per entry of each feed's newest release
(`Sport, Event, Gender, RankingDate, IsFederationDate, Position, ISO3, TeamName`).
`RankingReleases` and `RankingRows` hold every release ever seen; `IsFederationDate` says whether
`RankingDate` came from the federation or is the scrape date of a feed that publishes none.

## Configuration

`serviceconfig.json` lists the feeds; `appsettings.json` holds the connection string. Override it
outside development with the `ConnectionStrings__WorldRankGuesserConnection` environment variable.
````

- [ ] **Step 3: Update `CLAUDE.md`**

Replace the **Commands** code block with:

```powershell
docker compose up -d --wait                              # local SQL Server 2022 (sa / Rankings_Dev1!, port 1433)
dotnet tool restore                                      # repo-local dotnet-ef 8.0.31
dotnet ef database update --project SportsRankingService --startup-project SportsRankingService # apply migrations (the app never migrates itself)
dotnet build SportsRankingService.sln                    # build all projects (warnings are expected; 0 errors)
dotnet run --project SportsRankingService                # fetch every enabled feed once, save, exit (0 = all saved, 1 = a feed failed)
dotnet run --project WebScrapingBenchmarks --configuration Release   # run benchmarks (or WebScrapingBenchmarks\RunBenchmark.bat)
dotnet test SportsRankingService.Tests                   # parser, resolver, snapshot and repository tests; no network, SQLite in-memory for the repository
dotnet ef migrations add <Name> --project SportsRankingService --startup-project SportsRankingService --output-dir Persistence/Migrations   # after changing the entities
```

In the paragraph after it, replace the sentence starting "`serviceconfig.json` is copied next to the binaries" with: "`appsettings.json` and `serviceconfig.json` are copied next to the binaries and the host's content root is `AppContext.BaseDirectory`, so the process runs from any working directory. There is no timer: the process runs every enabled feed once and exits; schedule it weekly with Task Scheduler or cron (README)."

Replace the sentence "`Microsoft.EntityFrameworkCore.SqlServer` is pinned to 7.x on a net8.0 project; leave it unless upgrading deliberately." with "EF Core packages are 8.0.31 across the solution (SqlServer, Design, Sqlite in tests) and `dotnet-ef` is pinned to the same version in `.config/dotnet-tools.json`."

Replace the whole **Database** section with:

```markdown
## Persistence

Code-first EF Core in `SportsRankingService/Persistence/`; migrations live in `Persistence/Migrations/`. Two tables: `RankingReleases` (one immutable row per published ranking table per feed: sport, event, gender, `RankingDate`, `IsFederationDate`, SHA-256 `ContentHash`, `FirstSeenAt`, `LastSeenAt`) and `RankingRows` (`ReleaseId`, `Ordinal`, `Position`, `ISO3` varchar(3), `TeamName`). `RankingRepository.SaveAsync` hashes the snapshot (rows plus the federation date; a scrape date is excluded) and compares it with the feed's newest release (greatest `Id`): equal means only `LastSeenAt` moves (`Unchanged`); different means a new release with its rows (`Inserted`). Nothing is ever updated except `LastSeenAt`, so all releases are the history and the newest per feed is current. The `CurrentRankings` view (raw SQL in the initial migration, not mapped in EF) exposes the newest release per feed for the planned consumer app.

The connection string `WorldRankGuesserConnection` in `appsettings.json` points at the Docker Compose SQL Server with a committed dev password; override it elsewhere via the `ConnectionStrings__WorldRankGuesserConnection` environment variable. Repository tests run on SQLite in-memory through `EnsureCreated`, which is why "newest" is by `Id` and never by ordering on `FirstSeenAt` (SQLite cannot order by `DateTimeOffset`); the view is only verified against the real server.
```

In **Architecture**, replace the pipeline list with:

```markdown
**Pipeline per run** (`Program` → `RankingUpdater` → `RankingSourceRunner` → `IRankingRepository`):

1. `Program.cs` builds the generic host (configuration, logging, DI only; no hosted service), runs `IRankingUpdater.UpdateAllAsync` once and returns exit code 1 if any feed failed.
2. `RankingUpdater` runs every enabled item in parallel, catches per item, saves each snapshot through `IRankingRepository` from its own scope, and returns an `UpdateSummary` (feeds, inserted, unchanged, failed).
3. `RankingSourceRunner` resolves the URL (`IUrlResolver` → `ResolvedUrl(Url, RankingDate?)`), fetches it (`IHttpFetcher`), parses it (`IRankingParser` → `ParsedRanking(Entries, RankingDate?)`) and builds a `RankingSnapshot` via `RankingSnapshotBuilder`: entries ordered by position, `Take` applied by position, and the ranking date chosen as parser's date, else resolver's date, else today from `TimeProvider` with `IsFederationDate = false`. A failed fetch yields null; a malformed response throws `ParseException`; an unknown `Source` or `UrlResolver` name throws `InvalidOperationException` because that is a config error.
4. Parsers and resolvers are registered as plain `IRankingParser` / `IUrlResolver` singletons in `AddRankingPipeline`; the runner indexes them by `SourceName` / `Name`. Nothing outside `Persistence/` and `Program.cs` references EF Core.
```

In the **Parsers are pure functions** paragraph, change "`Parsing/IRankingParser` returns `RankEntry(Position, ISO3, TeamName?)`" to "`Parsing/IRankingParser` returns `ParsedRanking(Entries, RankingDate?)` of `RankEntry(Position, ISO3, TeamName?)`", and append: "Feeds that publish a ranking date override `GetRankingDate` on the base class and parse it with `IsoDate.Parse` (throws `ParseException` for a non-ISO value): ICC `rank_date`, WTA `rankedAt`, ESPN `update`, WBSC row `date`, FIBA's `rankingDatesselect` dropdown. FIH and Volleyball World only stamp file-generation time and return null. FIFA's date comes from the resolver (the `iso` of the chosen schedule entry; the digits in the id are not the ranking date) and WBSC's also from its resolver."

Replace the **Current state** section with:

```markdown
## Current state

Live on 2026-09-15: 29 feeds enabled and parsing (about 2,500 rows per run), 8 disabled. Disabled with notes in `serviceconfig.json`: the five BWF badminton feeds (Cloudflare returns 403 to .NET's TLS handshake while curl passes; parser and fixtures are ready), IIHF ice hockey (403), ATP doubles (Cloudflare challenge). Persistence was redesigned on 2026-09-15 (spec: `docs/superpowers/specs/2026-09-15-persistence-redesign-design.md`): releases with insert-on-change, federation ranking dates where feeds publish them, Docker Compose SQL Server, code-first migrations, run-once console. No consumer app exists yet; `CurrentRankings` is its intended read model.
```

Delete the final paragraph about "The September 2026 refactor left the superseded classes in place" (they are deleted in this task). If any deletion was refused in Tasks 8, 9 or 11, replace it with one sentence listing the blanked files to delete.

- [ ] **Step 4: Commit everything, including the previously staged deletions**

Run: `git status --short` and confirm the only entries are the staged deletions from before this work, the `CountryUtil.cs` modification, and the files touched in this task.

```bash
git add -A
git commit -m "Delete superseded files and document the persistence redesign" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 5: Final verification**

Run: `dotnet build SportsRankingService.sln`, `dotnet test SportsRankingService.Tests`, and `git status --short`
Expected: 0 errors; all pass except 3 skipped; clean tree.
