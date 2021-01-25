using System.Collections.Generic;

namespace Rxns.Playback
{
    public class InMemoryTapeRepo : ITapeRepository
    {
        readonly IDictionary<string, ITapeStuff> _tapes = new Dictionary<string, ITapeStuff>();

        public void Delete(string name)
        {
            _tapes.Remove(name);
        }

        public ITapeStuff GetOrCreate(string name, IStringCodec codec = null)
        {
            if (_tapes.ContainsKey(name)) return _tapes[name];
            else
            {
                var newTape = RxnTape.FromSource(name, new InMemoryTapeSource());
                _tapes.Add(name, newTape);
                return newTape;
            }
        }

        public IEnumerable<ITapeStuff> GetAll(string directory = "", string mask = "*.*",  IStringCodec codec = null)
        {
            return _tapes.Values;
        }
    }
}
