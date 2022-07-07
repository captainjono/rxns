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
