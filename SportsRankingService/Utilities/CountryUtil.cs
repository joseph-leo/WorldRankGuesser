using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Utilities
{
    internal static class CountryUtil
    {
        public static List<RegionInfo> GetCountries()
        {
            List<RegionInfo> regionInfoList = new();
            CultureInfo[] cultureInfo = CultureInfo.GetCultures(CultureTypes.SpecificCultures);

            foreach (CultureInfo culture in cultureInfo)
            {
                RegionInfo regionInfo = new(culture.Name);

                if (!regionInfoList.Any(x => x.Name == regionInfo.Name) && !regionInfo.TwoLetterISORegionName.Any(char.IsDigit))
                {
                    regionInfoList.Add(regionInfo);
                }
            }

            return regionInfoList;
        }

        public static string? GetCountryName(string ISO3)
        {
            List<RegionInfo> countries = CountryUtil.GetCountries();
            RegionInfo? country = countries.FirstOrDefault(x => x.ThreeLetterISORegionName == ISO3);
            string? countryName = null;

            if (country is not null)
            {
                countryName = country.EnglishName;
            }

            return countryName;
        }

        public static string GetISO3FromCountry(string countryName)
        {
            List<RegionInfo> countries = GetCountries();
            RegionInfo? country = countries.FirstOrDefault(x => x.EnglishName.ToLower() == GetRegionMapping(countryName));

            return country.ThreeLetterISORegionName;
        }

        public static string GetISO3FromCode(string countryCode, string countryName)
        {
            List<RegionInfo> countries = GetCountries();
            RegionInfo? country = countries.FirstOrDefault(x => x.ThreeLetterISORegionName == IOCToISO3(countryCode), null);
            country ??= countries.FirstOrDefault(x => x.EnglishName.ToLower() == GetRegionMapping(countryName), null);

            return country.ThreeLetterISORegionName;
        }

        private static string GetRegionMapping(string countryName)
        {
            if (CountryMappings.TryGetValue(countryName.ToLower(), out var mappedName))
            {
                return mappedName;
            }

            return countryName.ToLower();
        }

        private static readonly Dictionary<string, string> CountryMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "england", "united kingdom" },
            { "wales", "united kingdom" },
            { "northern ireland", "united kingdom" },
            { "scotland", "united kingdom" },
            { "chinese taipei", "taiwan" },
            { "czech republic", "czechia" },
            { "trinidad & tobago", "trinidad & tobago" },
            { "hong kong", "hong kong sar" },
            { "hong kong china", "hong kong sar" },
            { "macau", "macao sar" },
            { "people's republic of china", "china" },
            { "türkiye", "turkey" },
            { "united states of america", "united states" },
            { "usa", "united states" },
            { "uae", "united arab emirates" },
            { "tahiti", "french polynesia" }
        };

        public static string IOCToISO3(string IOC)
        {
            Dictionary<string, string> dictionary = new()
            {
                { "ALG", "DZA" },
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
            };

            return dictionary.ContainsKey(IOC) ? dictionary[IOC] : IOC;
        }
    }
}
