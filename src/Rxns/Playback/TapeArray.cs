using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns.Playback
{
    public class TapeArray<T> : ITapeArray<T> 
        where T : IRxn
    {
        private readonly string _baseTapeDir;
        private readonly ITapeRepository _tapeRepo;
        private readonly Func<T, string> _tapeSelector;
        private readonly IDictionary<string, ITapeStuff> _tapes;
        private readonly IDictionary<string, IRecording> _preparedTapes;

        public TapeArray(string baseTapeDir, ITapeRepository tapeRepo, Func<T, string> tapeSelector, IDictionary<string, ITapeStuff> tapes = null, int maxPreparedTapes = 2048 /*16384  maybe?*/)
        {
            _baseTapeDir = baseTapeDir;
            _tapeRepo = tapeRepo;
            _tapeSelector = tapeSelector;
            _preparedTapes = new UseConcurrentReliableOpsWhenCastToIDictionary<string, IRecording>(new ConcurrentDictionary<string, IRecording>());
            _tapes = tapes ?? new UseConcurrentReliableOpsWhenCastToIDictionary<string, ITapeStuff>(new ConcurrentDictionary<string, ITapeStuff>());
        }

        /// <summary>
        /// Loads existing tapes into memory to be ejected or recorded too. Call this before record
        /// </summary>
        public IObservable<Unit> Load()
        {
            return _tapeRepo.GetAll(_baseTapeDir).ForEach(Insert).ToObservable().Select(_ => new Unit());
        }

        /// <summary>
        /// Not thread safe;
        /// Records an event to a tape based on the tapeSelector
        /// </summary>
        /// <param name="event"></param>
        public void Record(T @event)
        {
            var selectedTape = _tapeSelector(@event);
            if (selectedTape == null)
            {
                GeneralLogging.Log.OnWarning("TapeArray", "Not recording {0}".FormatWith(@event.GetType()));
                return;
            }
            
            var tape = GetOrCreate("{0}/{1}".FormatWith(_baseTapeDir, selectedTape));
            var controls = PrepareTape(tape);
            controls.Record(@event);
        }

        /// <summary>
        /// Removes tapes from the array but doesnt delete them
        /// </summary>
        /// <returns></returns>
        public void EjectAll()
        {
            foreach(var prepared in _preparedTapes.Keys)
                Eject(prepared);

            _tapes.Clear();
        }

        private IRecording PrepareTape(ITapeStuff tape)
        {
            if (_preparedTapes.Count <= _tapes.Count && _preparedTapes.ContainsKey(tape.Name))
            {
                return _preparedTapes[tape.Name];
            }
            
            //evict tape - oldest. maybe we should evict based on last access time?
            if(_preparedTapes.Count > 0)
                Eject(_preparedTapes.Keys.FirstOrDefault());

            var newlyPrepared = tape.Source.StartRecording();
            _preparedTapes.Add(tape.Name, newlyPrepared);
            return newlyPrepared;
        }

        private void Eject(string evicted)
        {
            _preparedTapes[evicted].Dispose();
            _preparedTapes.Remove(evicted);
        }

        public void Delete(string name)
        {
            _tapeRepo.Delete(name);
        }

        public ITapeStuff GetOrCreate(string key, IStringCodec codec = null)
        {
            //the assumption here is the tape.Name and key are the same
            if (!_tapes.ContainsKey(key))
                Insert(_tapeRepo.GetOrCreate(key, codec));

            return _tapes[key];
        }

        public IEnumerable<ITapeStuff> GetAll(string directory = "", string mask = "*.*", IStringCodec codec = null)
        {
            return _tapeRepo.GetAll(directory, mask, codec);
        }

        private void Insert(ITapeStuff tape)
        {
            _tapes.Add(tape.Name, tape);
        }
    }
}
