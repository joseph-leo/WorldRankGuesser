using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SportsRankingService.Utilities
{
    public static class NullOrEmptyExtension
    {
        public static T NotNullOrEmpty<T>([NotNull] this T? self, [CallerArgumentExpression(nameof(self))] string callerExp = "") where T : class
        {
            if (IsCollectionAndEmpty(self)) throw new ArgumentNullException(callerExp + " contains no elements");

            if (self == null) throw new ArgumentNullException(callerExp);

            return self;
        }

        private static bool IsCollectionAndEmpty(object? obj)
        {
            if (obj is ICollection collection)
            {
                if (collection.Count == 0) return true;
            }

            return false;
        }
    }
}
