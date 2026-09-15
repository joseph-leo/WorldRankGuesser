using Newtonsoft.Json;
using SportsRankingService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Utilities
{
    public class ConfigHelper(string configName)
    {
        private IConfigurationRoot _config = GetConfig(configName);

        public static IConfigurationRoot GetConfig(string configName)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configName + ".json", optional: true, reloadOnChange: true)
                .Build();

            return config;
        }

        public T? GetConfigSection<T>(string sport) where T : class
        {
            T? urls = _config.GetSection(sport).Get<T>();

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

        //static Dictionary<string, object> ConvertToNestedDictionaries(Dictionary<string, object> dictionary)
        //{
        //    var result = new Dictionary<string, object>();

        //    foreach (var kvp in dictionary)
        //    {
        //        if (kvp.Value is Dictionary<string, object> nestedDict)
        //        {
        //            result[kvp.Key] = ConvertToNestedDictionaries(nestedDict);
        //        }
        //        else
        //        {
        //            result[kvp.Key] = kvp.Value;
        //        }
        //    }

        //    return result;
        //}
    }
}
