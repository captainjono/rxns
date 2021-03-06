﻿
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace System
{
    /// <summary>
    /// A collection of extension methods to help with differing reflection between the portable library and SL5
    /// </summary>
    public static class PortableReflectionExtensions
    {

        public static bool IsAssignableFrom(this Type t, Type c)
        {
            return t.GetTypeInfo().IsAssignableFrom(c.GetTypeInfo());
        }

        /// <summary>
        /// Determines whether this type is assignable to <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">The type to test assignability to.</typeparam>
        /// <returns>True if this type is assignable to references of type
        /// <typeparamref name="T" />; otherwise, False.</returns>
        public static bool IsAssignableTo<T>(this Type @this)
        {
            if (@this == null)
                throw new ArgumentNullException(nameof(@this));
            return typeof(T).IsAssignableFrom(@this);
        }

        public static Type[] GetGenericArguments(this Type t)
        {
            return t.GetTypeInfo().GenericTypeArguments;
        }



        public static IEnumerable<ConstructorInfo> GetConstructors(this Type t)
        {
            return t.GetTypeInfo().DeclaredConstructors;
        }

        public static IEnumerable<Type> GetInterfaces(this Type t)
        {
            return t.GetTypeInfo().ImplementedInterfaces;
        }

        public static IEnumerable<Type> GetTypes(this Assembly a)
        {
            return a.DefinedTypes.Select(t => t.AsType());
        }

        public static bool IsAbstract(this Type t)
        {
            return t.GetTypeInfo().IsAbstract;
        }

        public static bool IsInterface(this Type t)
        {
            return t.GetTypeInfo().IsInterface;
        }

        public static bool IsGenericType(this Type t)
        {
            return t.GetTypeInfo().IsGenericType;
        }

        public static MethodInfo GetMethod(this Type t, string name, Type[] parameters)
        {
            return t.GetRuntimeMethod(name, parameters);
        }
    }
}
