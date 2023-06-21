using System.Globalization;

namespace WorldRankGuesser.Helpers
{
    public static class CountryUtil
    {
        public static List<string> WestIndiesIOCs { get => GetWestIndiesIOCs(); }

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

        public static string GetFlag(string ISO2)
        {
            return IsoCountryCodeToFlagEmoji(ISO2);
        }

        private static string IsoCountryCodeToFlagEmoji(string ISO2) => string.Concat(ISO2.ToUpper().Select(x => char.ConvertFromUtf32(x + 0x1F1A5)));

        public static string GetISO3(string countryName)
        {
            List<RegionInfo> countries = GetCountries();
            RegionInfo? country = countries.FirstOrDefault(x => x.EnglishName.ToLower() == GetRegionMapping(countryName));

            return country.ThreeLetterISORegionName;
        }

        private static string GetRegionMapping(string countryName)
        {
            string formattedCountryName = countryName.ToLower();
            
            switch (formattedCountryName)
            {
                case "england":
                case "wales":
                case "northern ireland":
                case "scotland":
                    return "united kingdom";

                case "chinese taipei":
                    return "taiwan";

                case "czech republic":
                    return "czechia";

                case "trinidad &amp; tobago":
                    return "trinidad & tobago";

                case "hong kong": 
                case "hong kong china":
                    return "hong kong sar";

                case "macau":
                    return "macao sar";

                case "people's republic of china":
                    return "china";

                case "türkiye":
                    return "turkey";

                case "united states of america":
                    return "united states";

                case "uae":
                    return "united arab emirates";

                default:
                    return formattedCountryName;
            }
        }

        private static readonly string[] _westIndies =
        {
            "Antigua & Barbuda",
            "Barbados",
            "Dominica",
            "Grenada",
            "Guyana",
            "Jamaica",
            "St. Kitts & Nevis",
            "St. Lucia",
            "St. Vincent & Grenadines",
            "Trinidad & Tobago",
            "Sint Maarten",
            "Anguilla",
            "British Virgin Islands",
            "Montserrat",
            "U.S. Virgin Islands"
        };

        private static List<string> GetWestIndiesIOCs()
        {
            List<string> IOCs = new List<string>();

            foreach (string country in  _westIndies)
            {
                string IOC = GetISO3(country).ToIOC();
                IOCs.Add(IOC);
            }

            return IOCs;
        }
    }
}
