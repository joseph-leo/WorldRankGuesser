using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace SportsRankingService.Utilities
{
    //public class NestedDictionaryConverter : JsonConverter
    //{
    //    public override bool CanConvert(Type objectType)
    //    {
    //        return objectType == typeof(Dictionary<string, object>);
    //    }

    //    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    //    {
    //        if (reader.TokenType == JsonToken.StartObject)
    //        {
    //            var obj = serializer.Deserialize<Dictionary<string, object>>(reader);
    //            return obj;
    //        }
    //        else
    //        {
    //            return null; // or throw an exception if you want to handle other cases
    //        }
    //    }

    //    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
