using System;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public interface ITapeArray<T> : ITapeRepository
        where T : IRxn
    {
        IObservable<Unit> Load();

        /// <summary>
        /// Not thread safe;
        /// Records an event to a tape based on the tapeSelector
        /// </summary>
        /// <param name="event"></param>
        void Record(T @event);

        void EjectAll();
    }
}
