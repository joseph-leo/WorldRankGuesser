// Board statistics against the current rankings, for tuning Scoring:Cap, Game:MinCategoriesUnderCap and
// Game:MaxCapPicksInOptimal: how often the optimal assignment has picks scoring the cap, how high the optimal is,
// and what a redraw rule costs in draws and in which countries appear. Both rank modes are always reported.
//
// Read-only: one SELECT on dbo.CurrentCountryRankings, then everything in memory with the API's own snapshot
// builder, board generator and solver. No boards, games or players are written.
//
// Usage: dotnet run tools/SimulateBoards/simulate.cs -- [boards=20000] [cap=Scoring:Cap]
// Settings come from src/WorldRankGuesser.Api/appsettings.json; the connection string from
// appsettings.Development.json, or the ConnectionStrings__WorldRankGuesserConnection environment variable.
// This SDK's file-based apps disable reflection-based JsonSerializer by default; re-enable it here.
#:project ../../src/WorldRankGuesser.Api/WorldRankGuesser.Api.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

var apiDirectory = Path.GetFullPath(Path.Combine(ScriptDirectory(), "../../src/WorldRankGuesser.Api"));

using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json")));
var options = JsonSerializer.Deserialize<GameOptions>(settings.RootElement.GetProperty("Game").GetRawText())!;

var boards = args.Length > 0 ? int.Parse(args[0]) : 20000;
var cap = args.Length > 1 ? int.Parse(args[1]) : settings.RootElement.GetProperty("Scoring").GetProperty("Cap").GetInt32();

var rows = ReadRankings(ConnectionString(apiDirectory));
var snapshot = RankingsSnapshotBuilder.Build(rows, options, cap, CountryCatalog.LoadEmbedded(), DateTimeOffset.UtcNow);
var size = snapshot.Categories.Count;

Console.WriteLine($"{rows.Count} view rows, {snapshot.DrawableCountries.Count} drawable countries, cap {cap}, {boards} boards per mode, " +
    $"MinCategoriesUnderCap {options.MinCategoriesUnderCap}, MaxCapPicksInOptimal {options.MaxCapPicksInOptimal}");

foreach (var mode in Enum.GetValues<RankMode>())
{
    Report(mode);
}

void Report(RankMode mode)
{
    var pool = snapshot.DrawableCountries;
    var scoring = new ScoringOptions { RankMode = mode, Cap = cap };

    Console.WriteLine();
    Console.WriteLine($"================ {mode} mode ================");

    // How many categories each country scores under the cap in, and how many countries do so per category.
    var underCap = pool.ToDictionary(
        c => c.Iso3,
        c => snapshot.Categories.Count(category => ScoringEngine.Score(snapshot.Find(category.Id, c.Iso3), mode, cap).Score < cap));

    Console.WriteLine("Countries by number of categories under the cap:  " + string.Join("  ", Enumerable.Range(0, size + 1)
        .Select(k => $"{k}:{underCap.Values.Count(v => v == k)}")));
    Console.WriteLine("Countries under the cap, per category:            " + string.Join("  ", snapshot.Categories.Select(category =>
        $"{category.Id}:{pool.Count(c => ScoringEngine.Score(snapshot.Find(category.Id, c.Iso3), mode, cap).Score < cap)}")));

    // The redraw rule is switched off here (a limit of every pick), so each board is the generator's first draw and
    // the rules below can be compared on the same boards.
    var random = new Random(12345);
    var results = new List<Result>(boards);

    for (var b = 0; b < boards; b++)
    {
        var generated = BoardGenerator.Generate(snapshot, scoring, maxCapPicksInOptimal: size, random);
        var cells = generated.Content.Cells;
        var scores = cells.Select(IReadOnlyList<int> (row) => row.Select(cell => cell.Score).ToList()).ToList();

        // The fewest cap picks any assignment can have: the ones no player can avoid.
        var forced = OptimalAssignment.MinTotal(scores.Select(IReadOnlyList<int> (row) => row.Select(s => s >= cap ? 1 : 0).ToList()).ToList());

        var optimal = OptimalAssignment.Solve(scores).CategoryOfCountry;
        var unranked = optimal.Where((category, country) => cells[country][category].Unranked).Count();

        results.Add(new Result(
            generated.OptimalScore, generated.CapPicksInOptimal, forced, unranked, generated.Content.Countries.Select(c => c.Iso3).ToArray()));
    }

    Console.WriteLine();
    Console.WriteLine("Cap picks per board                               0       1       2       3      4+");
    Console.WriteLine("  in the optimal the results screen shows    " + Distribution(results, r => r.CapPicks));
    Console.WriteLine("  forced (no assignment avoids them)         " + Distribution(results, r => r.Forced));
    Console.WriteLine("  chosen (in the optimal, but avoidable)     " + Distribution(results, r => r.CapPicks - r.Forced));
    Console.WriteLine($"  cap picks that are truly unranked: {Percent(results.Sum(r => r.Unranked), results.Sum(r => r.CapPicks))} (the rest are ranked at or beyond {cap})");

    Console.WriteLine();
    Console.WriteLine("Optimal score                         boards     mean   p10   p50   p90   max   mean without cap picks");
    Describe("  all boards", results);
    for (var k = 0; k <= 3; k++)
    {
        var picks = k;
        Describe($"  {(picks == 3 ? "3+" : picks)} cap pick{(picks == 1 ? "" : "s")} in the optimal", results.Where(r => picks == 3 ? r.CapPicks >= 3 : r.CapPicks == picks).ToList());
    }

    Console.WriteLine();
    Console.WriteLine("Redraw rule                           accept   avg draws   mean opt   p50   p90   max");
    Rule("  cap picks in optimal = 0", results, r => r.CapPicks == 0);
    Rule("  cap picks in optimal <= 1", results, r => r.CapPicks <= 1);
    Rule("  cap picks in optimal <= 2", results, r => r.CapPicks <= 2);
    Rule("  forced cap picks = 0", results, r => r.Forced == 0);
    Rule("  optimal <= 200", results, r => r.Optimal <= 200);
    Rule("  optimal <= 300", results, r => r.Optimal <= 300);
    Rule("  optimal <= 400", results, r => r.Optimal <= 400);

    foreach (var limit in new[] { 0, 1 })
    {
        var accepted = results.Where(r => r.CapPicks <= limit).ToList();
        if (accepted.Count == 0) continue;

        var before = Rates(results);
        var after = Rates(accepted);
        var ratio = pool.ToDictionary(c => c.Iso3, c => before.TryGetValue(c.Iso3, out var rate) ? after.GetValueOrDefault(c.Iso3) / rate : 1);

        Console.WriteLine();
        Console.WriteLine($"Who appears with cap picks in optimal <= {limit}, relative to a uniform draw (1.00 = unchanged):");
        Console.WriteLine("  by categories under the cap:  " + string.Join("  ", Enumerable.Range(0, size + 1)
            .Where(k => underCap.Values.Any(v => v == k))
            .Select(k => $"{k}:{pool.Where(c => underCap[c.Iso3] == k).Average(c => ratio[c.Iso3]):0.00}")));
        Console.WriteLine($"  below 0.50: {ratio.Values.Count(v => v < 0.5)} of {pool.Count};  below 0.25: {ratio.Values.Count(v => v < 0.25)};  never: {ratio.Values.Count(v => v == 0)}");
        Console.WriteLine("  most suppressed: " + string.Join(", ", pool.OrderBy(c => ratio[c.Iso3]).Take(10).Select(c => $"{c.Name} {ratio[c.Iso3]:0.00}")));
        Console.WriteLine("  most boosted:    " + string.Join(", ", pool.OrderByDescending(c => ratio[c.Iso3]).Take(10).Select(c => $"{c.Name} {ratio[c.Iso3]:0.00}")));
    }
}

void Describe(string label, List<Result> results)
{
    if (results.Count == 0)
    {
        Console.WriteLine($"{label,-36}{0,8}");
        return;
    }

    var sorted = results.Select(r => r.Optimal).OrderBy(x => x).ToList();
    var withoutCapPicks = results.Average(r => r.Optimal - r.CapPicks * cap);
    Console.WriteLine($"{label,-36}{results.Count,8}{sorted.Average(),9:0.0}{Percentile(sorted, 0.1),6}{Percentile(sorted, 0.5),6}{Percentile(sorted, 0.9),6}{sorted[^1],6}{withoutCapPicks,12:0.0}");
}

static void Rule(string label, List<Result> results, Func<Result, bool> accept)
{
    var accepted = results.Where(accept).Select(r => r.Optimal).OrderBy(x => x).ToList();
    if (accepted.Count == 0)
    {
        Console.WriteLine($"{label,-36}{"0.0%",8}");
        return;
    }

    var rate = (double)accepted.Count / results.Count;
    Console.WriteLine($"{label,-36}{Percent(accepted.Count, results.Count),8}{1 / rate,12:0.0}{accepted.Average(),11:0.0}{Percentile(accepted, 0.5),6}{Percentile(accepted, 0.9),6}{accepted[^1],6}");
}

static Dictionary<string, double> Rates(List<Result> results) =>
    results.SelectMany(r => r.Countries).GroupBy(c => c).ToDictionary(g => g.Key, g => (double)g.Count() / results.Count);

static string Distribution(List<Result> results, Func<Result, int> value) =>
    string.Join("  ", Enumerable.Range(0, 5).Select(k => Percent(results.Count(r => k == 4 ? value(r) >= 4 : value(r) == k), results.Count).PadLeft(6)));

static string Percent(int part, int whole) => whole == 0 ? "n/a" : $"{100.0 * part / whole:0.0}%";

static int Percentile(List<int> sorted, double p) => sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(p * sorted.Count))];

static List<CountryRankingRow> ReadRankings(string connectionString)
{
    var rows = new List<CountryRankingRow>();

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    using var command = new SqlCommand(
        "SELECT Sport, Event, Gender, Position, ISO3, TeamName, Competitor FROM dbo.CurrentCountryRankings", connection);
    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        rows.Add(new CountryRankingRow
        {
            Sport = reader.GetString(0),
            Event = reader.IsDBNull(1) ? null : reader.GetString(1),
            Gender = reader.GetString(2),
            Position = reader.GetInt16(3),
            ISO3 = reader.GetString(4),
            TeamName = reader.IsDBNull(5) ? null : reader.GetString(5),
            Competitor = reader.IsDBNull(6) ? null : reader.GetString(6),
        });
    }

    return rows;
}

static string ConnectionString(string apiDirectory)
{
    var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__WorldRankGuesserConnection");
    if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

    using var development = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiDirectory, "appsettings.Development.json")));
    return development.RootElement.GetProperty("ConnectionStrings").GetProperty("WorldRankGuesserConnection").GetString()
        ?? throw new InvalidOperationException("No WorldRankGuesserConnection connection string.");
}

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

record Result(int Optimal, int CapPicks, int Forced, int Unranked, string[] Countries);
