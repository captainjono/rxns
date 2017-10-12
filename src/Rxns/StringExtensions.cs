using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace System
{
    public static class StringExtensions
    {
        public static bool IsNullOrWhitespace(this string str)
        {
            return String.IsNullOrWhiteSpace(str);
        }
        public static string IsNullOrWhitespace(this string str, string returnThis)
        {
            return str.IsNullOrWhitespace() ? returnThis : str;
        }

        public static string FormatWith(this string source, params object[] args)
        {
            return string.Format(source, args);
        }


        public static PropertyInfo GetPropertyDef<T>(this T typeToSet, string propertyName)
        {
            return typeToSet.GetType().GetProperties().FirstOrDefault(p => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<PropertyInfo> GetProperties(this Type t)
        {
            return t.GetRuntimeProperties();
        }


        public static bool BasicallyEquals(this string str, string compareTo)
        {
            return str.IsNullOrWhitespace() ? compareTo.IsNullOrWhitespace() : str.Equals(compareTo, StringComparison.OrdinalIgnoreCase);
        }

        public static bool BasicallyContains(this string str, string compareTo)
        {
            return str.IsNullOrWhitespace() ? compareTo.IsNullOrWhitespace() : str.IndexOf(compareTo, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string ToStringMax(this object str, int length)
        {
            if (str == null)
                return "";

            var @string = str.ToString();

            return @string.Substring(0, @string.Length >= length ? length : @string.Length);
        }

        public static bool Contains(this string str, string value, StringComparison comparision)
        {
            return str.IsNullOrWhitespace() ? value.IsNullOrWhitespace() : str.IndexOf(value, comparision) != -1;
        }

        public static string ReplaceIfTrue(this string context, bool condition, string value, string withNewValue)
        {
            return condition ? context.Replace(value, withNewValue) : context;
        }

        public static string ToStringOrNull(this object context)
        {
            return context == null ? null : context.ToString();
        }
    }
}
