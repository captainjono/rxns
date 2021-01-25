using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rxns.NewtonsoftJson
{
    public class SystemCommandCreationConverter : JsonConverter
    {
        public override bool CanWrite
        {
            get { return false; }
        }
        
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("SystemCommandCreationConverter should only be used while deserializing.");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jobj = JObject.Load(reader);
            var cmdObj = jobj["action"];
            int cmd;

            //command is being reveived but not received by the intergation manager
            //integration manager is being intialised properly, with good route
            //possibly the command is not being parsed properly by this, on download
            //need to check this.

            //if no action is supplied
            if (cmdObj == null)
                return new RemoteCommandEvent();

            if (!Int32.TryParse(cmdObj.ToString(), out cmd))
                throw new JsonSerializationException(String.Format("Unsupported command detected: {0}", jobj));

            var obj = Events.EventFactory.FromSystemCommand((SystemCommand) cmd) ?? new RemoteCommandEvent(); //we default to remotecommand if no other targets found by the factory

            serializer.Populate(jobj.CreateReader(), obj);

            return obj;
        }

        public override bool CanConvert(Type objectType)
        {
            return (typeof(RemoteCommandEvent)).IsAssignableFrom(objectType);
        }
    }
}
