using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Rxns.Collections;

namespace Rxns.Playback
{
    public class InMemoryTapeRepo : ITapeRepository
    {
        private readonly Func<string, ITapeSource> _createSource;
        readonly IDictionary<string, ITapeStuff> _tapes = new UseConcurrentReliableOpsWhenCastToIDictionary<string, ITapeStuff>(new ConcurrentDictionary<string, ITapeStuff>());

        public InMemoryTapeRepo(Func<string, ITapeSource> createSource)
        {
            _createSource = createSource;
        }

        public void Delete(string name)
        {
            _tapes.Remove(name);
        }

        public ITapeStuff GetOrCreate(string name, IStringCodec codec = null)
        {
            if (_tapes.ContainsKey(name)) return _tapes[name];
            else
            {
                var newTape = RxnTape.FromSource(name, _createSource(name));
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