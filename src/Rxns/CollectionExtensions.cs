using System.Linq;

namespace System.Collections.Generic
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// returns a flattened string representation of the list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="delimiter"></param>
        /// <returns></returns>
        public static string ToStringEach<T>(this IEnumerable<T> list, string delimiter = ",")
        {
            return list != null ? String.Join(delimiter, list) : null;
        }


        public static T[] ForEach<T>(this IEnumerable<T> items, Action<T> action)
        {
            if (items == null) return default(T[]);
            var context = items as T[] ?? items.ToArray();
            if (!context.AnyItems())
                return context;

            foreach (var item in context)
            {
                var i = item;
                action(i);
            }

            return context;
        }

        /// <summary>
        /// Returns true is replaced, otherwise false
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <returns>If the list has any elements</returns>
        public static bool AddOrReplace<Tk, Tv>(this IDictionary<Tk, Tv> dict, Tk key, Tv value)
        {
            if (dict.ContainsKey(key))
            {
                dict[key] = value;
                return true;
            }

            dict.Add(key, value);
            return false;
        }

        public static IList<To> AddOrReplace<To>(this IList<To> list, To obj)
        {
            if (list.Contains(obj))
                list.Remove(obj);

            list.Add(obj);

            return list;
        }

        public static IList<To> RemoveIfExists<To>(this IList<To> list, To obj)
        {
            if (list.Contains(obj))
                list.Remove(obj);

            return list;
        }

        public static bool RemoveIfExists<Tk, Tv>(this IDictionary<Tk, Tv> dict, Tk key)
        {
            if (dict.ContainsKey(key))
            {
                dict.Remove(key);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The same as Any, but works with nulls
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <returns>If the list has any elements</returns>
        public static bool AnyItems<T>(this IEnumerable<T> list)
        {
            return list != null && list.Any();
        }

        /// <summary>
        /// The same as the Length property, but works with nulls
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <returns></returns>
        public static int Length<T>(this IEnumerable<T> list)
        {
            return list == null ? 0 : list.Count();
        }
    }
}

