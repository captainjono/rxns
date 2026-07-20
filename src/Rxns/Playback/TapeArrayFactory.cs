using System;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class TapeArrayFactory : ITapeArrayFactory
    {
        private readonly ITapeRepository _repo;

        public TapeArrayFactory(ITapeRepository repo)
        {
            _repo = repo;
        }

        public ITapeArray<T> Create<T>(string baseDir, Func<T, string> tapeSelector, int maxPreparedTapes = 2048 /*16384  maybe?*/) where T : IRxn
        {
            return new TapeArray<T>(baseDir, _repo, tapeSelector, null, maxPreparedTapes);
        }
    }
}
