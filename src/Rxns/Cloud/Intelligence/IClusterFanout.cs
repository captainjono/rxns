using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns.Cloud.Intelligence
{
    public interface IClusterWorker<T, TR>
    {
        string Name { get; }
        string Route { get; }
        IDictionary<string, string> Info { get; }
        IObservable<TR> DoWork(T work);
        IObservable<bool> IsBusy { get; }
        void Update(IDictionary<string, string> eventInfo);
    }

    public interface IClusterFanout<T, TR> : IRxnPublisher<IRxn> where TR : IRxn
    {
        IDisposable RegisterWorker(IClusterWorker<T, TR> worker);
        void Fanout(T cfg);
        IDictionary<string, WorkerConnection<T, TR>> Workers { get; }
    }

    public class WorkerConnection<T, TR>
    {
        public IClusterWorker<T, TR> Worker { get; set; }
        public IDisposable DoWork { get; set; }

        /// <summary>
        /// Synchronously flipped to <c>true</c> by
        /// <see cref="CompeteFanout{T,TR}.Fanout"/> when this connection is
        /// picked, and back to <c>false</c> when its DoWork chain
        /// terminates (completion, error, or unsubscribe).
        ///
        /// <para>
        /// Closes the phase 7n4 race: <c>IClusterWorker.IsBusy</c> publishes
        /// asynchronously when the worker actually starts running its
        /// dispatch — production saw two Fanout calls 60ms apart both pick
        /// the same idle worker because IsBusy hadn't flipped yet, leaving
        /// the second worker idle. <c>IsReserved</c> is owned by the
        /// fanout itself and serves as the synchronous "this slot is
        /// taken" marker.
        /// </para>
        /// </summary>
        public bool IsReserved { get; set; }
    }

    public class WorkerInfoUpdated : IRxn
    {
        public string Name { get; set; }
        public IDictionary<string, string> Info { get; set; }
    }

    public class WorkerDiscovered<T, TR> : IRxn
    {
        public IClusterWorker<T, TR> Worker { get; set; }
    }


    public class WorkerDisconnected : IRxn
    {
        public string Name { get; set; }
    }
}
