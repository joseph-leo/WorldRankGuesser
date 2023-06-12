namespace WorldRankGuesser.Helpers
{
    static class RandomExtensions
    {
        public static void Shuffle<T> (this Random rng, List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }
        }
    }
}
