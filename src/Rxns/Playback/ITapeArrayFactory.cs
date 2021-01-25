using System;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public interface ITapeArrayFactory
    {
        ITapeArray<T> Create<T>(string baseDir, Func<T, string> tapeSelector, int maxPreparedTapes = 2048 /*16384  maybe?*/) where T : IRxn;
    }
}
