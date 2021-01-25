using Rxns.Playback;

namespace Rxns.Hosting
{
    public class UseDeserialiseCodec : IStringCodec
    {
        public T FromString<T>(string encoded)
        {
            return encoded.Deserialise<T>();
        }

        public string ToString<T>(T unencoded)
        {
            return unencoded.Serialise();
        }

        public char Delimiter { get { return ''; } }

        public bool SkipErrors { get; private set; }

        public UseDeserialiseCodec()
        {

        }

        public UseDeserialiseCodec(bool skipErrors)
        {
            SkipErrors = skipErrors;
        }
    }
}
