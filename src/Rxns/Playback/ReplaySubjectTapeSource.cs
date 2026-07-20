using System;
using System.Reactive.Subjects;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class SubjectRecorder : IRecording
    {
        private readonly ISubject<ICapturedRxn> _contents;

        public SubjectRecorder(ISubject<ICapturedRxn> contents)
        {
            _contents = contents;
        }

        public void Dispose()
        {

        }

        public void Record(IRxn rxn, TimeSpan? sinceBeginning = null)
        {
            _contents.OnNext(new CapturedRxn(TimeSpan.Zero, rxn));
        }

        public void FlushNow()
        {

        }
    }


    public class ReplaySubjectTapeSource : ITapeSource
    {
        private readonly ReplaySubject<ICapturedRxn> _contents;

        public ReplaySubjectTapeSource(int maxSize)
        {
            _contents = new ReplaySubject<ICapturedRxn>(maxSize);
        }

        public void Dispose()
        {
            if (_contents.IsDisposed) return;

            _contents.OnCompleted();
            _contents.Dispose();
        }

        public void Rewind()
        {

        }

        public void SkipTo(Func<IRxn, bool> selector = null)
        {

        }

        public IRecording StartRecording()
        {
            return new SubjectRecorder(_contents);
        }

        public IObservable<ICapturedRxn> Contents => _contents;
        public TimeSpan Duration { get; }
    }

}
