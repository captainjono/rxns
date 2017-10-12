using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public interface IStringCodec
    {
        T FromString<T>(string encoded);
        string ToString<T>(T unencoded);
        char Delimiter { get; }
        /// <summary>
        /// If the codec encounters an error, should it fail or
        /// try to skip the error and continue reading
        /// </summary>
        bool SkipErrors { get; }
    }


    public interface ITapeStuff
    {
        ITapeSource Source { get; }
        string Name { get; }
        string Hash { get; }
    }

    public interface IFileTapeSource : ITapeSource
    {
        IFileMeta File { get; }
    }

    public interface ITapeSource : IDisposable
    {
        IEnumerable<ICapturedRxn> Contents { get; }
        void Rewind();
        void SkipTo(Func<IRxn, bool> selector = null);
        IRecording StartRecording();
        TimeSpan Duration { get; }

    }

    public interface ITapeRepository
    {
        void Delete(string name);

        ITapeStuff GetOrCreate(string name, IStringCodec codec = null);

        IEnumerable<ITapeStuff> GetAll(string directory = "", IStringCodec codec = null);
    }

    public interface ICapturedRxn : IRxn
    {
        TimeSpan Offset { get; }

        IRxn Recorded { get; }
    }
    
    /// <summary>
    /// Represents a buffered recording session for events.
    /// The duration of the recording is device specific,
    /// but generally it will start when it is first created,
    /// and it is stopped when it is disposed of
    /// </summary>
    public interface IRecording : IDisposable
    {
        /// <summary>
        /// Writes the rxn. The writting is buffered and will only fully
        /// complete the operation when disposed of, or when FlushNow is called
        /// </summary>
        /// <param name="rxn"></param>
        /// <param name="sinceBeginning"></param>
        void Record(IRxn rxn, TimeSpan? sinceBeginning = null);
        void FlushNow();
    }
}
