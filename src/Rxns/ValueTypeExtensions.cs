using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Rxns.Exceptions;
using Rxns.System.Collections.Generic;

namespace Rxns
{
    public static class ValueTypeExtensions
    {
        private static List<string> size_suffixes = new List<string> { " B", " kb", " MB", " GB", " TB", " PB" };

        public static string ToFileSize(this int number)
        {
            return ToFileSize((long)number);
        }

        public static string ToFileSize(this long number)
        {
            for (int i = 0; i < size_suffixes.Count; i++)
            {
                var temp = number / (int)Math.Pow(1024, i + 1);
                if (temp == 0)
                    return (number / (int)Math.Pow(1024, i)) + size_suffixes[i];
            }
            return number.ToString();
        }

        public static string ToFileSize(this float number)
        {
            for (int i = 0; i < size_suffixes.Count; i++)
            {
                var temp = number / (int)Math.Pow(1024, i + 1);
                if (temp == 0)
                    return (number / (int)Math.Pow(1024, i)) + size_suffixes[i];
            }
            return number.ToString();
        }

        public static T SetProperty<T, TV>(this T typeToSet, Expression<Func<T, TV>> propertyName, object value)
        {
            var member = propertyName.Body as MemberExpression;
            if (member == null) throw new ReflectionException("{0}.{1} not found", typeof(T).Name, propertyName);
            typeToSet.SetProperty(member.Member.Name, value);

            return typeToSet;
        }

        public static void SetProperty<T>(this T typeToSet, string propertyName, object value)
        {
            var propertyTokens = propertyName.Split('.');
            object objectToSet = typeToSet;
            PropertyInfo property = null;

            objectToSet = typeToSet.FindPropertyWithExpression(out property, propertyTokens);

            if (property == null) throw new ReflectionException("{0}.{1} not found", typeof(T).Name, propertyName);
            property.SetValue(objectToSet, value, null);
        }

        public static T CopyTo<T>(this T source, T target)
        {
            foreach (var property in source.GetType().GetProperties())
                if (property.CanWrite) target.SetProperty(property.Name, source.GetProperty(property.Name));

            return target;
        }
        public static bool CompareTo(this object source, object target)
        {
            foreach (var propert in source.GetType().GetProperties())
                if (!target.GetProperty(propert.Name).Equals(source.GetProperty(propert.Name)))
                    return false;

            return true;
        }

        public static bool CompareToUsing<TI>(this object source, object target)
        {
            foreach (var propert in typeof(TI).GetProperties())
                if (!target.GetProperty(propert.Name).Equals(source.GetProperty(propert.Name)))
                    return false;

            return true;
        }
        public static object CopyToUsing<TI>(this object source, object target)
        {
            PropertyInfo def;
            foreach (var property in typeof(TI).GetProperties())
            {
                def = target.GetPropertyDef(property.Name);
                if (def == null) throw new NotImplementedException("Target object does not confom to {0}, missing property {1}".FormatWith(typeof(TI), property.Name));
                if (def.CanWrite) target.SetProperty(property.Name, source.GetProperty(property.Name));
            }

            return target;
        }

        public static object FindPropertyWithExpression<T>(this T typeToSet, out PropertyInfo property, string[] propertyTokens)
        {
            if (propertyTokens.Length == 1)
            {
                property = typeToSet.GetPropertyDef(propertyTokens[0]);
                return typeToSet;
            }

            var nextClass = typeToSet.GetProperty(propertyTokens[0]);
            return nextClass.FindPropertyWithExpression(out property, propertyTokens.Skip(1).ToArray());
        }

        public static void SetProperty(this object typeToSet, string propertyName, object value)
        {
            var property = typeToSet.GetPropertyDef(propertyName);
            if (property == null) return;

            if (value == null)
            {
                property.SetValue(typeToSet, null, null);
                return;
            }

            if (property.PropertyType != value.GetType())
            {
                var t = property.PropertyType;

                if (t.GetTypeInfo().IsGenericType && t.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
                {
                    if (value == null) return;
                    t = Nullable.GetUnderlyingType(t);
                }
                value = Convert.ChangeType(value, t, CultureInfo.CurrentCulture);
            }

            property.SetValue(typeToSet, value, null);
        }

        public static object GetProperty(this object typeToSet, string propertyName)
        {
            var property = typeToSet.GetPropertyDef(propertyName);
            return property.GetValue(typeToSet, null);
        }

        public static object GetProperty<T>(this T typeToSet, string propertyName)
        {
            var property = typeToSet.GetPropertyDef(propertyName);
            if (property == null) return null;
            return property.GetValue(typeToSet, null);
        }


        public static bool IsAssignableTo<T>(this Type @this)
        {
            if (@this == null)
                throw new ArgumentNullException("this");
            else
                return typeof(T).IsAssignableFrom(@this);
        }

        public static bool IsAssignableTo(this Type @this, Type expected)
        {
            if (@this == null)
                throw new ArgumentNullException("this");
            else
                return expected.IsAssignableFrom(@this);
        }

        /// <summary>
        /// Invokes the method on the target object, only if the target object defines the method explicity and all parameter types match. 
        /// ie this method will fail
        /// - if the method target is defined on a base class
        /// - if the method target defines parameters whos types dont explicictly match the supplied
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="targetObject"></param>
        /// <param name="methodName"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static object Invoke<T>(this T targetObject, string methodName, params object[] args)
        {
            //need to find the method based on the type --> tried to match the params with the supplied params, no worky. maybe cast to 
            var method = targetObject.GetType().GetMethod(methodName, args.Select(s => s.GetType()).ToArray());

            if (method == null) throw new ReflectionException("{0}.{1}({2}) not found", targetObject.GetType().Name, methodName, args.Select(a => a.GetType().Name).ToStringEach());

            return method.Invoke(targetObject, args);
        }

        /// <summary>
        /// Invokes a method on the target object, using fallback stratergies where the exact method cannot be located. 
        /// If this is not the desired behaviour, please use Invoke(T)
        /// 
        /// Backup stratergy
        /// - if it fails to find the appropriote method defined on the target class, it will search methods that have been inherited from any base classes
        /// - if a method is found but its parameter types dont exactly match, it will see if a method exists which defines params
        /// that are "assignableTo" fromt he given parameters
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="targetObject"></param>
        /// <param name="methodName"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static object InvokeReliably<T>(this T targetObject, string methodName, params object[] args)
        {
            var method = targetObject.GetType().GetMethod(methodName, args.Select(s => s.GetType()).ToArray());

            if (method == null) method = targetObject.GetType().GetMembers(methodName).FirstOrDefault(m => m.GetParameters().Select(s => s.ParameterType).AreCompatibleTypesWithOrder(args.Select(s => s.GetType())));
            if (method == null) throw new ReflectionException(String.Format("Method {0}.{1}({2}) not found", targetObject.GetType().Name, methodName, args.ToStringEach()));

            return method.Invoke(targetObject, args);
        }

        public static MethodInfo[] GetMembers(this Type context, string methodName)
        {
            var allTypes = context.GetTypeInfo().DeclaredNestedTypes;

            return allTypes.SelectMany(t => t.GetDeclaredMethods(methodName)).ToArray();
        }

        public static bool ImplementsInterface<T>(this object source)
        {
            return source.GetType().GetInterfaces().Any(i => i == typeof(T));
        }
        public static bool ImplementsInterface(this object source, Type target)
        {
            return source.GetType().GetInterfaces().Any(i => i == target);
        }

        public static bool ImplementsInterfaceReliably(this Type target, Type type)
        {
            var found = target.GetInterfaces().Any(i => i == type);

            if (found) return true;

            return target.GetTypeInfo().BaseType != null ? target.GetTypeInfo().BaseType.ImplementsInterfaceReliably(type) : false;
        }

        public static bool ImplementsInterfaceReliably<T>(this object source)
        {
            return source.GetType().ImplementsInterfaceReliably(typeof(T));
        }


        public static ExpandoObject AddPropertyWithValue(this ExpandoObject target, string propertName, object value)
        {
            ((IDictionary<string, object>)target)[propertName] = value;

            return target;
        }

        public static bool AreCompatibleTypesWithOrder(this IEnumerable<Type> a, IEnumerable<Type> b)
        {
            if (a.Length() != b.Length()) return false;

            for (var i = 0; i < a.Length(); i++)
            {
                if (!a.ElementAt(i).IsAssignableTo(b.ElementAt(i)) && !a.ElementAt(i).IsAssignableFrom(b.ElementAt(i))) return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the property is defined in the object.
        /// Note: propertName is case sensitive
        /// </summary>
        /// <param name="source"></param>
        /// <param name="propertyName">The case-sensitive property name to look for/param>
        /// <returns>If it is defined or not</returns>
        public static bool HasProperty(this object source, string propertyName)
        {
            return source.GetType().GetProperties().Any(p => p.Name.BasicallyEquals(propertyName));
        }
        
        public static bool HasAttribute<TAttribute>(this object type, string propertyName) where TAttribute : Attribute
        {
            return type.GetType().GetRuntimeProperties().Any(pi => pi.Name == propertyName && pi.GetCustomAttributes<TAttribute>(true).Any());
        }

        public static int AsInt(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return default(int);

            return Int32.Parse(obj);
        }

        public static float AsFloat(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return default(float);

            return float.Parse(obj);
        }

        public static int? AsNullableInt(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return Int32.Parse(obj);
        }

        public static long AsLong(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return default(int);

            return long.Parse(obj);
        }

        public static long? AsNullableLong(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return long.Parse(obj);
        }

        public static Guid? AsGuid(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return Guid.Parse(obj);
        }

        public static bool AsBool(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return default(bool);

            return Boolean.Parse(obj);
        }

        public static bool? AsNullableBool(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return Boolean.Parse(obj);
        }

        public static DateTime AsDateTime(this string obj)
        {
            return String.IsNullOrWhiteSpace(obj) ? default(DateTime) : DateTime.Parse(obj);
        }
        public static DateTime? AsNullableDateTime(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return DateTime.Parse(obj);
        }


        public static TimeSpan AsTimeSpan(this string obj)
        {
            return String.IsNullOrWhiteSpace(obj) ? default(TimeSpan) : TimeSpan.Parse(obj);
        }

        public static TimeSpan? AsNullableTimeSpan(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return TimeSpan.Parse(obj);
        }

        public static bool ToBool(this object obj)
        {
            return obj == null || String.IsNullOrWhiteSpace(obj.ToString()) ? default(Boolean) : Boolean.Parse(obj.ToString());
        }


        public static Decimal AsDecimal(this string obj)
        {
            return String.IsNullOrWhiteSpace(obj) ? default(Decimal) : Decimal.Parse(obj);
        }

        public static Decimal? AsNullableDecimal(this string obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj))
                return null;

            return Decimal.Parse(obj);
        }

        public static T AsEnum<T>(this string value) where T : struct
        {
            if (!typeof(T).GetTypeInfo().IsEnum) throw new ArgumentException("T must be an enum");

            return (T)Enum.Parse(typeof(T), value);
        }

    }
}

namespace Rxns.Exceptions
{

    public class ReflectionException : Exception
    {
        public ReflectionException(string message, params object[] args)
            : base(string.Format(message, args))
        {

        }
    }
}
