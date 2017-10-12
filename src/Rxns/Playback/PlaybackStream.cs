using System;
using System.Reactive.Subjects;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class PlaybackStream
    {
        /// <summary>
        /// The name of the stream being played back
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The current position in the total time of the stream
        /// </summary>
        public IObservable<TimeSpan> Position { get; private set; }

        public ISubject<bool> IsPaused { get; private set; }

        /// <summary>
        /// The stream being played
        /// </summary>
        public IObservable<IRxn> Stream { get; private set; }

        //play
        //pause
        //ffwd?

        public PlaybackStream(string name, IObservable<IRxn> stream, IObservable<TimeSpan> position, ISubject<bool> isPaused)
        {
            Stream = stream;
            Position = position;
            IsPaused = isPaused;
            Name = name;
        }
    }
}
