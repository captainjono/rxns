using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using Rxns.DDD.BoundedContext;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Playback
{
    /// <summary>
    /// Plays tapes which contain IDomainEvents into repositories in a reliable way
    /// </summary>
    public interface IRxnTapeToTenantRepoPlaybackAutomator
    {
        void Play(string tapeDir, Func<string, IAggRoot> getById, Action<IAggRoot, IEnumerable<IDomainEvent>> save, ITapeRepository tapes, IFileSystemService fs, Action<ITapeStuff, ITapeRepository, Exception> onError = null);
    }

    public class PlaybackException : Exception
    {
        public string TapeName { get; private set; }

        public PlaybackException(string tapeName, Exception e)
            : base("Unable to playback {0}: {1}".FormatWith(tapeName, e.Message), e)
        {
            TapeName = tapeName;
        }
    }
}
