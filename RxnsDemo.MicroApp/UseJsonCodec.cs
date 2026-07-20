using Newtonsoft.Json;
using Rxns.Playback;

namespace RxnsDemo.Micro
{
    public class NewtonSoftJsonCodec : IStringCodec
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            Formatting = Formatting.None,
            TypeNameHandling = TypeNameHandling.All,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public T FromString<T>(string encoded)
        {
            return JsonConvert.DeserializeObject<T>(encoded, _settings);
        }

        public string ToString<T>(T unencoded)
        {
            return JsonConvert.SerializeObject(unencoded, _settings);
        }

        // Framing uses '\n' so tape files are newline-delimited JSON, directly
        // consumable by jq, PowerShell Get-Content|ConvertFrom-Json, etc. Old tapes
        // framed with the legacy '' char are still readable because
        // RxnsStream.ReadStream treats '' as an equivalent frame boundary on read.
        public char Delimiter { get { return '\n'; } }

        public bool SkipErrors { get; private set; }

        public NewtonSoftJsonCodec()
        {

        }

        public NewtonSoftJsonCodec(bool skipErrors)
        {
            SkipErrors = skipErrors;
        }
    }
}
