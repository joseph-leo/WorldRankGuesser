using System.Text.Json;

namespace WorldRankGuesser.Api.Countries;

/// <summary>ISO 3166 alpha-3 to alpha-2, from the committed countries.json. Flags are keyed by alpha-2.</summary>
public sealed class CountryCatalog(IReadOnlyDictionary<string, string> iso2ByIso3)
{
    public static CountryCatalog LoadEmbedded()
    {
        using var stream = typeof(CountryCatalog).Assembly.GetManifestResourceStream("countries.json")
            ?? throw new InvalidOperationException("Embedded resource countries.json is missing.");

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("countries.json is empty.");

        return new CountryCatalog(map);
    }

    public bool TryGetIso2(string iso3, out string iso2)
    {
        if (iso2ByIso3.TryGetValue(iso3, out var found))
        {
            iso2 = found;
            return true;
        }

        iso2 = string.Empty;
        return false;
    }
}
