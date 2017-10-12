using Newtonsoft.Json;

namespace Rxns.Playback
{
    public class FromJsonCodec : IStringCodec
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            Formatting = Formatting.None,
            TypeNameHandling = TypeNameHandling.Objects | TypeNameHandling.Arrays,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        public T FromString<T>(string encoded)
        {
            return encoded.FromJson<T>();
        }

        public string ToString<T>(T unencoded)
        {
            return JsonConvert.SerializeObject(unencoded, _settings);
        }

        public char Delimiter { get { return ''; } }

        public bool SkipErrors { get; private set; }

        public FromJsonCodec()
        {

        }

        public FromJsonCodec(bool skipErrors)
        {
            SkipErrors = skipErrors;
        }
    }
}
