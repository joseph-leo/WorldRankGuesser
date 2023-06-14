using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace WorldRankGuesser.Helpers
{
    public static class GeneralUtil
    {
        public static string ReadConfig(string jsonName)
        {
            return File.ReadAllText(string.Format("wwwroot/{0}.json", jsonName));
        }
    }
}
