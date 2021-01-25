using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Interfaces;
using Rxns.Scheduling;

namespace Rxns.NewtonsoftJson
{
    public class TaskCreationConverter : JsonConverter
    {
        private readonly ITaskFactory _taskFactory;

        public override bool CanWrite
        {
            get { return false; }
        }

        public TaskCreationConverter(ITaskFactory taskFactory)
        {
            _taskFactory = taskFactory;
        }
        
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("TaskCreationConverter should only be used while deserializing.");
        }
        
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jobj = JObject.Load(reader);
            var plugin = jobj["PluginName"];

            if (plugin == null)
                throw new JsonSerializationException(String.Format("Unsupported task detected, please specify a PluginName that maps to a .Net type.Name: {0}", jobj.ToString()));

            var obj = _taskFactory.Get<ISchedulableTask>(plugin.ToString());

            if (obj == null)
                throw new JsonSerializationException(String.Format("Cannot locate the task type '{0}'. Ensure plugin has been loaded into the system", plugin));

            serializer.Populate(jobj.CreateReader(), obj);

            return obj;
        }

        public override bool CanConvert(Type objectType)
        {
            return (typeof (ISchedulableTask)).IsAssignableFrom(objectType);
        }
    }
}
