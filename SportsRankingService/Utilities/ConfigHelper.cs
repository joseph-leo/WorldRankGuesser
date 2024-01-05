using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Utilities
{
    public class ConfigHelper
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>? Urls;

        public ConfigHelper(string configName)
        {
            Urls = ParseConfig(GetConfig(configName));
        }

        public static IConfigurationRoot GetConfig(string configName)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configName + ".json", optional: true, reloadOnChange: true)
                .Build();

            return config;
        }

        private static Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>> ParseConfig(IConfigurationRoot config)
        {
            var urls = config.GetSection("URLs").Get<Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>>();

            return urls;
        }

        //static Dictionary<string, object> DeserializeToNestedDictionaries(string jsonString)
        //{
        //    var rootObject = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString, new JsonSerializerSettings
        //    {
        //        Converters = { new NestedDictionaryConverter() }
        //    });

        //    return ConvertToNestedDictionaries(rootObject);
        //}

        static Dictionary<string, object> ConvertToNestedDictionaries(Dictionary<string, object> dictionary)
        {
            var result = new Dictionary<string, object>();

            foreach (var kvp in dictionary)
            {
                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    result[kvp.Key] = ConvertToNestedDictionaries(nestedDict);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }
    }
}
