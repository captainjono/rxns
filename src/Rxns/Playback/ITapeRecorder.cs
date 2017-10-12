using System;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public interface ITapeRecorder
    {
        PlaybackStream Play(ITapeStuff tape, PlaybackSettings settings = null);
        IDisposable Record(ITapeStuff tape, IObservable<IRxn> stream);
    }
}
