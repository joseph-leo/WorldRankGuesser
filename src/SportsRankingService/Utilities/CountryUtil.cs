using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace SportsRankingService.Utilities
{
    /// <summary>
    /// Country code helpers. Feeds identify countries three ways: ISO3 codes, IOC codes
    /// (<see cref="IOCToISO3"/>), or names in various spellings (<see cref="TryGetISO3FromCountry"/>).
    /// Name lookup is by a normalized key (lower case, diacritics and punctuation removed, "&amp;" as "and")
    /// against every region the runtime knows plus an alias table for spellings federations use.
    /// The name stored for a code is the reverse direction and comes only from <see cref="CountryNames"/>.
    /// </summary>
    internal static class CountryUtil
    {
        private static readonly Lazy<List<RegionInfo>> Regions = new(LoadRegions);
        private static readonly Lazy<FrozenDictionary<string, string>> NameIndex = new(BuildNameIndex);

        public static List<RegionInfo> GetCountries() => Regions.Value;

        /// <summary>The display name every stored row carries for <paramref name="code"/>; see <see cref="CountryNames"/>.</summary>
        public static bool TryGetCountryName(string code, out string? name) =>
            CountryNames.ByCode.TryGetValue(code, out name);

        public static string GetCountryName(string code) =>
            TryGetCountryName(code, out string? name)
                ? name!
                : throw new ArgumentException($"No country name for code '{code}'", nameof(code));

        /// <summary>Resolves a country name, slug ("cote-divoire") or alias ("usa", "England") to ISO3.</summary>
        public static bool TryGetISO3FromCountry(string? countryName, out string? iso3)
        {
            iso3 = null;

            if (string.IsNullOrWhiteSpace(countryName))
            {
                return false;
            }

            string key = Normalize(countryName);

            return Aliases.TryGetValue(key, out iso3) || NameIndex.Value.TryGetValue(key, out iso3);
        }

        public static string GetISO3FromCountry(string countryName) =>
            TryGetISO3FromCountry(countryName, out string? iso3)
                ? iso3!
                : throw new ArgumentException($"No ISO3 mapping for country name '{countryName}'", nameof(countryName));

        public static string IOCToISO3(this string ioc) =>
            IocToIso3.GetValueOrDefault(ioc, ioc);

        private static List<RegionInfo> LoadRegions()
        {
            List<RegionInfo> regions = [];

            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                RegionInfo region = new(culture.Name);

                if (regions.All(x => x.Name != region.Name) && !region.TwoLetterISORegionName.Any(char.IsDigit))
                {
                    regions.Add(region);
                }
            }

            return regions;
        }

        private static FrozenDictionary<string, string> BuildNameIndex()
        {
            Dictionary<string, string> index = [];

            foreach (RegionInfo region in Regions.Value)
            {
                string iso3 = region.ThreeLetterISORegionName;

                foreach (string name in new[] { region.EnglishName, region.NativeName, region.DisplayName, iso3 })
                {
                    index.TryAdd(Normalize(name), iso3);
                }
            }

            return index.ToFrozenDictionary();
        }

        /// <summary>Lower case, diacritics stripped, "&amp;" read as "and", everything but letters and digits removed.</summary>
        internal static string Normalize(string name)
        {
            string decomposed = name.Replace("&", " and ").Normalize(NormalizationForm.FormD);
            StringBuilder sb = new(decomposed.Length);

            foreach (char c in decomposed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Spellings used by federations that the runtime's region names do not cover, keyed by
        /// <see cref="Normalize"/>d form. Sub-national teams map to their sovereign state (England -> GBR).
        /// Kosovo has no ISO3 code; XKX is the widely used user-assigned code.
        /// </summary>
        private static readonly FrozenDictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["england"] = "GBR",
            ["wales"] = "GBR",
            ["scotland"] = "GBR",
            ["northernireland"] = "GBR",
            ["greatbritain"] = "GBR",
            ["usa"] = "USA",
            ["unitedstatesofamerica"] = "USA",
            ["uae"] = "ARE",
            ["korea"] = "KOR",
            ["southkorea"] = "KOR",
            ["chinesetaipei"] = "TWN",
            ["taiwan"] = "TWN",
            ["hongkong"] = "HKG",
            ["hongkongchina"] = "HKG",
            ["macau"] = "MAC",
            ["macao"] = "MAC",
            ["macauchina"] = "MAC",
            ["bruneidarussalam"] = "BRN",
            ["frenchpolynesiatahiti"] = "PYF",
            ["nationalolympiccommiteekenya"] = "KEN",   // BWF's spelling
            ["northernmarianas"] = "MNP",
            ["syrianarabrepublic"] = "SYR",
            ["peoplesrepublicofchina"] = "CHN",
            ["czechrepublic"] = "CZE",
            ["czechia"] = "CZE",
            ["turkey"] = "TUR",
            ["turkiye"] = "TUR",
            ["tahiti"] = "PYF",
            ["trinidadandtobago"] = "TTO",
            ["cotedivoire"] = "CIV",
            ["ivorycoast"] = "CIV",
            ["congodr"] = "COD",
            ["drcongo"] = "COD",
            ["democraticrepublicofthecongo"] = "COD",
            ["congo"] = "COG",
            ["stvincentandthegrenadines"] = "VCT",
            ["saintvincentandthegrenadines"] = "VCT",
            ["antiguabarbuda"] = "ATG",
            ["antiguaandbarbuda"] = "ATG",
            ["centralafricanrep"] = "CAF",
            ["centralafricanrepublic"] = "CAF",
            ["federatedstatesofmicronesia"] = "FSM",
            ["micronesia"] = "FSM",
            ["virginislands"] = "VIR",
            ["usvirginislands"] = "VIR",
            ["britishvirginislands"] = "VGB",
            ["newcaledonia"] = "NCL",
            ["kosovo"] = "XKX",
            ["palestine"] = "PSE",
            ["turksandcaicos"] = "TCA",
            ["bosniaandherzegovina"] = "BIH",
            ["northmacedonia"] = "MKD",
            ["capeverde"] = "CPV",
            ["caboverde"] = "CPV",
            ["eswatini"] = "SWZ",
            ["swaziland"] = "SWZ",
            ["russia"] = "RUS",
            ["iran"] = "IRN",
            ["syria"] = "SYR",
            ["laos"] = "LAO",
            ["vietnam"] = "VNM",
            ["brunei"] = "BRN",
            ["moldova"] = "MDA",
            ["bolivia"] = "BOL",
            ["venezuela"] = "VEN",
            ["tanzania"] = "TZA",
            ["myanmar"] = "MMR",
            ["burma"] = "MMR",
            ["saotomeandprincipe"] = "STP",
        }.ToFrozenDictionary();

        /// <summary>
        /// IOC codes plus the federation-specific codes that differ from both IOC and ISO3
        /// (Volleyball World, FIFA, ICC, FIH), all resolved to ISO3.
        /// </summary>
        private static readonly FrozenDictionary<string, string> IocToIso3 = new Dictionary<string, string>
        {
            { "AGU", "AIA" },   // Volleyball World
            { "CUR", "CUW" },   // Volleyball World
            { "FAR", "FRO" },   // Volleyball World
            { "MSH", "MHL" },   // Volleyball World
            { "MLD", "MDA" },   // Volleyball World
            { "PAU", "PLW" },   // Volleyball World
            { "GDP", "GLP" },   // Volleyball World
            { "MQE", "MTQ" },   // Volleyball World
            { "NMI", "MNP" },   // Volleyball World
            { "JSY", "JEY" },   // ICC
            { "GSY", "GGY" },   // ICC
            { "IOM", "IMN" },   // ICC
            { "STH", "SHN" },   // ICC (St. Helena)
            { "CTA", "CAF" },   // FIFA
            { "EQG", "GNQ" },   // FIFA
            { "TAH", "PYF" },   // FIFA (Tahiti)
            { "ESW", "SWZ" },   // ICC, FIH
            { "SDA", "SAU" },   // ICC
            { "ALG", "DZA" },
            { "ROM", "ROU" },
            { "SER", "SRB" },
            { "KOS", "XKX" },
            { "ASA", "ASM" },
            { "ANG", "AGO" },
            { "ANT", "ATG" },
            { "ARU", "ABW" },
            { "BAH", "BHS" },
            { "BRN", "BHR" },
            { "BAN", "BGD" },
            { "BAR", "BRB" },
            { "BIZ", "BLZ" },
            { "BER", "BMU" },
            { "BHU", "BTN" },
            { "BOT", "BWA" },
            { "IVB", "VGB" },
            { "BRU", "BRN" },
            { "BUL", "BGR" },
            { "BUR", "BFA" },
            { "CAM", "KHM" },
            { "CAY", "CYM" },
            { "CHA", "TCD" },
            { "CHI", "CHL" },
            { "CGO", "COG" },
            { "CRC", "CRI" },
            { "CRO", "HRV" },
            { "DEN", "DNK" },
            { "ESA", "SLV" },
            { "GEQ", "GNQ" },
            { "FIJ", "FJI" },
            { "GAM", "GMB" },
            { "GER", "DEU" },
            { "GRE", "GRC" },
            { "GRN", "GRD" },
            { "GUA", "GTM" },
            { "GUI", "GIN" },
            { "GBS", "GNB" },
            { "HAI", "HTI" },
            { "HON", "HND" },
            { "INA", "IDN" },
            { "IRE", "IRL" },
            { "IRI", "IRN" },
            { "KUW", "KWT" },
            { "LAT", "LVA" },
            { "LIB", "LBN" },
            { "LES", "LSO" },
            { "LBA", "LBY" },
            { "MAD", "MDG" },
            { "MAW", "MWI" },
            { "MAS", "MYS" },
            { "MTN", "MRT" },
            { "MRI", "MUS" },
            { "MON", "MCO" },
            { "MGL", "MNG" },
            { "MYA", "MMR" },
            { "NEP", "NPL" },
            { "NED", "NLD" },
            { "NCA", "NIC" },
            { "NIG", "NER" },
            { "NGR", "NGA" },
            { "OMA", "OMN" },
            { "PLE", "PSE" },
            { "PAR", "PRY" },
            { "PHI", "PHL" },
            { "POR", "PRT" },
            { "PUR", "PRI" },
            { "SKN", "KNA" },
            { "VIN", "VCT" },
            { "SAM", "WSM" },
            { "KSA", "SAU" },
            { "SEY", "SYC" },
            { "SIN", "SGP" },
            { "SLO", "SVN" },
            { "SOL", "SLB" },
            { "RSA", "ZAF" },
            { "SRI", "LKA" },
            { "SUD", "SDN" },
            { "SUI", "CHE" },
            { "TPE", "TWN" },
            { "TAN", "TZA" },
            { "TOG", "TGO" },
            { "TGA", "TON" },
            { "TRI", "TTO" },
            { "UAE", "ARE" },
            { "ISV", "VIR" },
            { "URU", "URY" },
            { "VAN", "VUT" },
            { "VIE", "VNM" },
            { "ZAM", "ZMB" },
            { "ZIM", "ZWE" }
        }.ToFrozenDictionary();
    }
}
