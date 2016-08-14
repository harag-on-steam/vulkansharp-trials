using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vulkan.Tutorial
{
    public static class CollectionExtensions
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> enumerable)
        {
            return new HashSet<T>(enumerable);
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> enumerable, IEqualityComparer<T> comparer)
        {
            return new HashSet<T>(enumerable, comparer);
        }

        public static SortedSet<T> ToSortedSet<T>(this IEnumerable<T> enumerable)
        {
            return new SortedSet<T>(enumerable);
        }

        public static SortedSet<T> ToSortedSet<T>(this IEnumerable<T> enumerable, IComparer<T> comparer)
        {
            return new SortedSet<T>(enumerable, comparer);
        }

        public static IEnumerable<T> CheckAvailability<T>(this IEnumerable<T> required, string name, IEnumerable<T> available)
        {
            var missing = required.Except(available).ToHashSet();
            if (missing.Count > 0)
                throw new InvalidOperationException($"The {name} [{string.Join(", ", missing)}] are required but not available.");
            return required;
        }
    }
}
