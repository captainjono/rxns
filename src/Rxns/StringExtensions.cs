using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace System
{
    public static class StringExtensions
    {
        public static bool IsNullOrWhitespace(this string str)
        {
            return String.IsNullOrWhiteSpace(str);
        }
        
        public static string IsNullOrWhiteSpace(this string str, string returnThis = null)
        {
            return String.IsNullOrWhiteSpace(str) ? returnThis : str;
        }

        public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T> context)
        {
            if (context == null)
                return new T[] { };
            else
                return context;
        }
        

        public static string FormatWith(this string source, params object[] args)
        {
            return String.Format(source, args);
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

        public static Stream ToStream(this string contents)
        {
            var stream = new StreamWriter(new MemoryStream());
            stream.Write(contents);
            stream.Flush();
            stream.BaseStream.Position = 0;
            return stream.BaseStream;
        }

        public static Regex _passwordExpression = new Regex(@"password=[^;]*;|key[=:].*|password:[^;]*", RegexOptions.IgnoreCase);
        public static string Sanatise(this string input)
        {
            return _passwordExpression.Replace(input, "(sanatised)");
        }


        public static string AsCrossPlatformPath(this string path)
        {
            return path.Replace("\\", "/");
        }

        public static string CrossPathCombine(this string path, params string[] dirs)
        {
            return Path.Combine(new[] {path}.Concat(dirs).ToArray()).AsCrossPlatformPath();
        }
    }
}
