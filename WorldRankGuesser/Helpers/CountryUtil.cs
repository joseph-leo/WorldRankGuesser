using System.Globalization;

namespace WorldRankGuesser.Helpers
{
    public static class CountryUtil
    {
        public static List<RegionInfo> GetCountries()
        {
            List<RegionInfo> regionInfoList = new();
            CultureInfo[] cultureInfo = CultureInfo.GetCultures(CultureTypes.SpecificCultures);

            foreach (CultureInfo culture in cultureInfo)
            {
                RegionInfo regionInfo = new(culture.Name);

                if (regionInfoList.SingleOrDefault(x => x.Name == regionInfo.Name) == null && !regionInfo.TwoLetterISORegionName.Any(char.IsDigit))
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
            RegionInfo? country = countries.FirstOrDefault(x => x.EnglishName == GetRegionMapping(countryName));

            return country.ThreeLetterISORegionName;
        }

        private static string GetRegionMapping(string countryName)
        {
            string formattedCountryName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(countryName.ToLower());
            
            switch (formattedCountryName)
            {
                case "England":
                case "Wales":
                case "Northern Ireland":
                case "Scotland":
                    return "United Kingdom";
                case "Chinese Taipei":
                    return "Taiwan";
                case "Czech Republic":
                    return "Czechia";
                case "Trinidad &Amp; Tobago":
                    return "Trinidad & Tobago";
                case "Hong Kong China":
                    return "Hong Kong SAR";
                case "Macau":
                    return "Macao SAR";
                default:
                    return formattedCountryName;
            }
        }

        public static string GetIOCMapping(string ISO3)
        {
            var dictionary = new Dictionary<string, string>();
            dictionary.Add("DZA", "ALG");
            dictionary.Add("ASM", "ASA");
            dictionary.Add("AGO", "ANG");
            dictionary.Add("ATG", "ANT");
            dictionary.Add("ABW", "ARU");
            dictionary.Add("BHS", "BAH");
            dictionary.Add("BHR", "BRN");
            dictionary.Add("BGD", "BAN");
            dictionary.Add("BRB", "BAR");
            dictionary.Add("BLZ", "BIZ");
            dictionary.Add("BMU", "BER");
            dictionary.Add("BTN", "BHU");
            dictionary.Add("BWA", "BOT");
            dictionary.Add("VGB", "IVB");
            dictionary.Add("BRN", "BRU");
            dictionary.Add("BGR", "BUL");
            dictionary.Add("BFA", "BUR");
            dictionary.Add("KHM", "CAM");
            dictionary.Add("CYM", "CAY");
            dictionary.Add("TCD", "CHA");
            dictionary.Add("CHL", "CHI");
            dictionary.Add("COG", "CGO");
            dictionary.Add("CRI", "CRC");
            dictionary.Add("HRV", "CRO");
            dictionary.Add("DNK", "DEN");
            dictionary.Add("SLV", "ESA");
            dictionary.Add("GNQ", "GEQ");
            dictionary.Add("FJI", "FIJ");
            dictionary.Add("GMB", "GAM");
            dictionary.Add("DEU", "GER");
            dictionary.Add("GRC", "GRE");
            dictionary.Add("GRD", "GRN");
            dictionary.Add("GTM", "GUA");
            dictionary.Add("GIN", "GUI");
            dictionary.Add("GNB", "GBS");
            dictionary.Add("HTI", "HAI");
            dictionary.Add("HND", "HON");
            dictionary.Add("IDN", "INA");
            dictionary.Add("IRN", "IRI");
            dictionary.Add("KWT", "KUW");
            dictionary.Add("LVA", "LAT");
            dictionary.Add("LBN", "LIB");
            dictionary.Add("LSO", "LES");
            dictionary.Add("LBY", "LBA");
            dictionary.Add("MDG", "MAD");
            dictionary.Add("MWI", "MAW");
            dictionary.Add("MYS", "MAS");
            dictionary.Add("MRT", "MTN");
            dictionary.Add("MUS", "MRI");
            dictionary.Add("MCO", "MON");
            dictionary.Add("MNG", "MGL");
            dictionary.Add("MMR", "MYA");
            dictionary.Add("NPL", "NEP");
            dictionary.Add("NLD", "NED");
            dictionary.Add("NIC", "NCA");
            dictionary.Add("NER", "NIG");
            dictionary.Add("NGA", "NGR");
            dictionary.Add("OMN", "OMA");
            dictionary.Add("PSE", "PLE");
            dictionary.Add("PRY", "PAR");
            dictionary.Add("PHL", "PHI");
            dictionary.Add("PRT", "POR");
            dictionary.Add("PRI", "PUR");
            dictionary.Add("KNA", "SKN");
            dictionary.Add("VCT", "VIN");
            dictionary.Add("WSM", "SAM");
            dictionary.Add("SAU", "KSA");
            dictionary.Add("SYC", "SEY");
            dictionary.Add("SGP", "SIN");
            dictionary.Add("SVN", "SLO");
            dictionary.Add("SLB", "SOL");
            dictionary.Add("ZAF", "RSA");
            dictionary.Add("LKA", "SRI");
            dictionary.Add("SDN", "SUD");
            dictionary.Add("CHE", "SUI");
            dictionary.Add("TWN", "TPE");
            dictionary.Add("TZA", "TAN");
            dictionary.Add("TGO", "TOG");
            dictionary.Add("TON", "TGA");
            dictionary.Add("TTO", "TRI");
            dictionary.Add("ARE", "UAE");
            dictionary.Add("VIR", "ISV");
            dictionary.Add("URY", "URU");
            dictionary.Add("VUT", "VAN");
            dictionary.Add("VNM", "VIE");
            dictionary.Add("ZMB", "ZAM");
            dictionary.Add("ZWE", "ZIM");

            return dictionary.ContainsKey(ISO3) ? dictionary[ISO3] : ISO3;
        }
    }
}
