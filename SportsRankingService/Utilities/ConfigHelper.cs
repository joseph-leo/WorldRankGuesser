using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Utilities
{
    public class ConfigHelper
    {
        public ConfigHelper(string configName)
        {
            Urls = ParseConfig(GetConfig(configName));
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, string>>>? Urls;

        public static IConfigurationRoot GetConfig(string configName)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configName + ".json", optional: true, reloadOnChange: true)
                .Build();

            return config;
        }

        private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> ParseConfig(IConfigurationRoot config)
        {
            var urls = config.GetSection("URLs").Get<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>();

            return urls;
        }
    }
}
