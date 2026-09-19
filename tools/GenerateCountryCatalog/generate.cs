// One-off generator for the committed ISO3 -> ISO2 table. Re-run only to add countries.
// Usage: dotnet run tools/GenerateCountryCatalog/generate.cs -- <countries.json> [<iso2.json>]
// This SDK's file-based apps disable reflection-based JsonSerializer by default; re-enable it here.
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Globalization;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: generate.cs -- <countries.json> [<iso2.json>]");
    return 1;
}

var iso2ByIso3 = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
{
    var region = new RegionInfo(culture.Name);
    var iso2 = region.TwoLetterISORegionName;
    if (iso2.Length != 2 || iso2.Any(char.IsDigit)) continue;   // skips "001", "150", "419"
    iso2ByIso3.TryAdd(region.ThreeLetterISORegionName, iso2);
}

// RegionInfo calls Kosovo XKK; the scraper and the federations use XKX.
iso2ByIso3["XKX"] = "XK";

var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(args[0], JsonSerializer.Serialize(iso2ByIso3, options) + "\n");
Console.WriteLine($"{iso2ByIso3.Count} countries -> {args[0]}");

if (args.Length == 2)
{
    var iso2List = iso2ByIso3.Values.Distinct().Order(StringComparer.Ordinal).ToArray();
    File.WriteAllText(args[1], JsonSerializer.Serialize(iso2List) + "\n");
    Console.WriteLine($"{iso2List.Length} ISO2 codes -> {args[1]}");
}

return 0;
