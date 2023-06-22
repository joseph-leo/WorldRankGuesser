using System.Globalization;
using System.Linq;

namespace WorldRankGuesser.Helpers
{
    public static class CountryUtil
    {
        public static List<string> WestIndiesISO3s { get => GetWestIndiesISO3s(); }

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
                case "usa":
                    return "united states";

                case "uae":
                    return "united arab emirates";

                case "tahiti":
                    return "french polynesia";

                default:
                    return formattedCountryName;
            }
        }

        public static string IOCToISO3(string IOC)
        {
            Dictionary<string, string> dictionary = new();
            dictionary.Add("ALG", "DZA");
            dictionary.Add("ASA", "ASM");
            dictionary.Add("ANG", "AGO");
            dictionary.Add("ANT", "ATG");
            dictionary.Add("ARU", "ABW");
            dictionary.Add("BAH", "BHS");
            dictionary.Add("BRN", "BHR");
            dictionary.Add("BAN", "BGD");
            dictionary.Add("BAR", "BRB");
            dictionary.Add("BIZ", "BLZ");
            dictionary.Add("BER", "BMU");
            dictionary.Add("BHU", "BTN");
            dictionary.Add("BOT", "BWA");
            dictionary.Add("IVB", "VGB");
            dictionary.Add("BRU", "BRN");
            dictionary.Add("BUL", "BGR");
            dictionary.Add("BUR", "BFA");
            dictionary.Add("CAM", "KHM");
            dictionary.Add("CAY", "CYM");
            dictionary.Add("CHA", "TCD");
            dictionary.Add("CHI", "CHL");
            dictionary.Add("CGO", "COG");
            dictionary.Add("CRC", "CRI");
            dictionary.Add("CRO", "HRV");
            dictionary.Add("DEN", "DNK");
            dictionary.Add("ESA", "SLV");
            dictionary.Add("GEQ", "GNQ");
            dictionary.Add("FIJ", "FJI");
            dictionary.Add("GAM", "GMB");
            dictionary.Add("GER", "DEU");
            dictionary.Add("GRE", "GRC");
            dictionary.Add("GRN", "GRD");
            dictionary.Add("GUA", "GTM");
            dictionary.Add("GUI", "GIN");
            dictionary.Add("GBS", "GNB");
            dictionary.Add("HAI", "HTI");
            dictionary.Add("HON", "HND");
            dictionary.Add("INA", "IDN");
            dictionary.Add("IRI", "IRN");
            dictionary.Add("KUW", "KWT");
            dictionary.Add("LAT", "LVA");
            dictionary.Add("LIB", "LBN");
            dictionary.Add("LES", "LSO");
            dictionary.Add("LBA", "LBY");
            dictionary.Add("MAD", "MDG");
            dictionary.Add("MAW", "MWI");
            dictionary.Add("MAS", "MYS");
            dictionary.Add("MTN", "MRT");
            dictionary.Add("MRI", "MUS");
            dictionary.Add("MON", "MCO");
            dictionary.Add("MGL", "MNG");
            dictionary.Add("MYA", "MMR");
            dictionary.Add("NEP", "NPL");
            dictionary.Add("NED", "NLD");
            dictionary.Add("NCA", "NIC");
            dictionary.Add("NIG", "NER");
            dictionary.Add("NGR", "NGA");
            dictionary.Add("OMA", "OMN");
            dictionary.Add("PLE", "PSE");
            dictionary.Add("PAR", "PRY");
            dictionary.Add("PHI", "PHL");
            dictionary.Add("POR", "PRT");
            dictionary.Add("PUR", "PRI");
            dictionary.Add("SKN", "KNA");
            dictionary.Add("VIN", "VCT");
            dictionary.Add("SAM", "WSM");
            dictionary.Add("KSA", "SAU");
            dictionary.Add("SEY", "SYC");
            dictionary.Add("SIN", "SGP");
            dictionary.Add("SLO", "SVN");
            dictionary.Add("SOL", "SLB");
            dictionary.Add("RSA", "ZAF");
            dictionary.Add("SRI", "LKA");
            dictionary.Add("SUD", "SDN");
            dictionary.Add("SUI", "CHE");
            dictionary.Add("TPE", "TWN");
            dictionary.Add("TAN", "TZA");
            dictionary.Add("TOG", "TGO");
            dictionary.Add("TGA", "TON");
            dictionary.Add("TRI", "TTO");
            dictionary.Add("UAE", "ARE");
            dictionary.Add("ISV", "VIR");
            dictionary.Add("URU", "URY");
            dictionary.Add("VAN", "VUT");
            dictionary.Add("VIE", "VNM");
            dictionary.Add("ZAM", "ZMB");
            dictionary.Add("ZIM", "ZWE");

            return dictionary.ContainsKey(IOC) ? dictionary[IOC] : IOC;
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

        private static List<string> GetWestIndiesISO3s()
        {
            List<string> ISO3s = new List<string>();

            foreach (string country in  _westIndies)
            {
                string ISO3 = GetISO3FromCountry(country);
                ISO3s.Add(ISO3);
            }

            return ISO3s;
        }
    }
}
