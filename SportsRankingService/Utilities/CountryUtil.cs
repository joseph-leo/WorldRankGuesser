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
    }
}
