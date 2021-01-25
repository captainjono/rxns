using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rxns.NewtonsoftJson
{
    public class EventsJsonConverter : JsonConverter
    {
        private IComponentContext _resolveEventByName;

        public override bool CanWrite
        {
            get { return false; }
        }

        public EventsJsonConverter(IComponentContext typeResolver)
        {
            _resolveEventByName = typeResolver;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("EventsJsonConverter should only be used while deserializing.");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jobj = JObject.Load(reader);
            var typeName = jobj["T"] ?? jobj["t"];

            if (typeName == null)
                throw new JsonSerializationException(String.Format("Please specify a PluginName with a json property called 'T':'{0}' that maps to the fullname of the event type", jobj.ToString()));

            var obj = _resolveEventByName.Resolve<object>(typeName.ToString());
            if (obj == null) throw new JsonSerializationException(String.Format("Cannot locate the event type '{0}'. Ensure event had been registered with the container/factory", typeName));

            serializer.Populate(jobj.CreateReader(), obj);

            return obj;
        }

        //need to create a new converter to create the emailattachment based on the __type

        public override bool CanConvert(Type objectType)
        {
            return (typeof(IEvent)).IsAssignableFrom(objectType);
        }
    }
}
