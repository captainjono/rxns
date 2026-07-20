using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rxns.NewtonsoftJson
{
    /// <summary>
    /// Legacy converter for system commands. Types it depended on (RemoteCommandEvent,
    /// SystemCommand, Events.EventFactory) are no longer in Rxns core.
    /// Kept as a no-op for backward compatibility.
    /// </summary>
    public class SystemCommandCreationConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("SystemCommandCreationConverter should only be used while deserializing.");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jobj = JObject.Load(reader);
            var obj = Activator.CreateInstance(objectType);
            serializer.Populate(jobj.CreateReader(), obj);
            return obj;
        }

        public override bool CanConvert(Type objectType)
        {
            return false; // Disabled — legacy types no longer available
        }
    }
}
