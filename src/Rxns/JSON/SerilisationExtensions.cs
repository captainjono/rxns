using System;
using System.Dynamic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Rxns.NewtonsoftJson
{
    public static class JsonExtensions
    {
        public static JsonSerializerSettings DefaultSettings = new JsonSerializerSettings() { ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor, TypeNameHandling = TypeNameHandling.Auto, ContractResolver = new NonPubicPropertiesResolver() };

        private static readonly JsonSerializerSettings defaultsTo = new JsonSerializerSettings() { Formatting = Formatting.Indented, TypeNameHandling = TypeNameHandling.Auto, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, SerializationBinder = new DynamicTypeRenamingSerializationBinder() };
        public static string ToJson(this object str, ITraceWriter serialisationLog = null)
        {
            defaultsTo.TraceWriter = serialisationLog;
            return JsonConvert.SerializeObject(str, defaultsTo);
        }

        private static readonly JsonSerializerSettings defaultsFrom = new JsonSerializerSettings() { ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, TypeNameHandling = TypeNameHandling.Auto, ContractResolver = new NonPubicPropertiesResolver(), SerializationBinder = new DynamicTypeRenamingSerializationBinder() };
        public static T FromJson<T>(this string json, ITraceWriter deserialisationLog = null)
        {
            defaultsFrom.TraceWriter = deserialisationLog;
            var obj = JsonConvert.DeserializeObject<T>(json, defaultsFrom);
            return obj;
        }

        public static object FromJson(this string json, Type targetType = null, ITraceWriter deserialisationLog = null, JsonSerializerSettings settings = null)
        {
            var s = settings ?? DefaultSettings;
            s.TraceWriter = deserialisationLog;
            return targetType == null ? JsonConvert.DeserializeObject(json, s) : JsonConvert.DeserializeObject(json, targetType, s);
        }


        public class DynamicTypeRenamingSerializationBinder : DefaultSerializationBinder
        {
            private static readonly DefaultSerializationBinder BaseInstance =
                new DefaultSerializationBinder();

            internal static readonly DynamicTypeRenamingSerializationBinder Instance =
                new DynamicTypeRenamingSerializationBinder(true);

            private readonly ISerializationBinder _binder;

            public DynamicTypeRenamingSerializationBinder() : this(false) { }

            private DynamicTypeRenamingSerializationBinder(bool useStaticBaseInstance)
            {
                _binder = useStaticBaseInstance ? BaseInstance : new DefaultSerializationBinder();
            }

            public override Type BindToType(string assemblyName, string typeName)
            {
                if (typeName.StartsWith("<>f__AnonymousType")) return typeof(ExpandoObject);

                return _binder.BindToType(assemblyName, typeName);
            }

            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                if (serializedType.FullName.StartsWith("<>f__AnonymousType") || serializedType.Name.Equals("ExpandoObject"))
                // It's either a dynamic type as a result of projection or an
                // anonymous type perhaps constructed by a controller (e.g, a "Lookups" object)
                //
                // These two predicates would work for dynamic types but not anonymous types
                // if (serializedType.Assembly.IsDynamic)
                // if (serializedType.BaseType == typeof(Breeze.ContextProvider.DynamicTypeBase))
                {
                    assemblyName = "Dynamic";
                    typeName = serializedType.Name;
                }
                else
                {
                    _binder.BindToName(serializedType, out assemblyName, out typeName);
                }
            }
        }

        public class NonPubicPropertiesResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);
                var pi = member as PropertyInfo;
                if (pi != null)
                {
                    prop.Readable = (pi.GetMethod != null);
                    prop.Writable = (pi.SetMethod != null);
                }
                return prop;
            }
        }
    }
}
