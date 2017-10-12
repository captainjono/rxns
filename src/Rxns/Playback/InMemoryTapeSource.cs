using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class ListRecorder : IRecording
    {
        private readonly List<ICapturedRxn> _store;

        public ListRecorder(List<ICapturedRxn> store)
        {
            _store = store;
        }

        public void Dispose()
        {
        }

        public void Record(IRxn rxn, TimeSpan? sinceBeginning = null)
        {
            _store.Add(new CapturedRxn(TimeSpan.Zero, rxn));
        }
        public void FlushNow()
        {

        }
    }

    /// <summary>
    /// Does not implement time-duration-rewind-skipto
    /// </summary>
    public class InMemoryTapeSource : ITapeSource
    {
        public TimeSpan Duration { get; private set; }
        public IEnumerable<ICapturedRxn> Contents { get { return _contents; } }

        private readonly List<ICapturedRxn> _contents = new List<ICapturedRxn>();

        public InMemoryTapeSource()
        {
        }

        public IRecording StartRecording()
        {
            return new ListRecorder(_contents);
        }

        public void Rewind()
        {
        }

        public void SkipTo(Func<IRxn, bool> selector = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
