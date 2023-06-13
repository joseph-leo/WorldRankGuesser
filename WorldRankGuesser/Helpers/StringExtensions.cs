namespace WorldRankGuesser.Helpers
{
    static class StringExtensions
    {
        public static string ConvertPathToUri(this string path)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.Combine(baseDir, path);
            string Uri = new Uri(fullPath).AbsoluteUri;
            return Uri;
        }
            
    }
}
