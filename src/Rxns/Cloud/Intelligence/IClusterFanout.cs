﻿using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns.Cloud.Intelligence
{
    public interface IClusterWorker<T, TR>
    {
        string Name { get; }
        string Route { get; }
        IObservable<TR> DoWork(T work);

        IObservable<bool> IsBusy { get; }
    }

    public interface IClusterFanout<T, TR> where TR : IRxn
    {
        void Attach(Action<IRxn> workCompletedHandler);
        IDisposable RegisterWorker(IClusterWorker<T, TR> worker);
        void Fanout(T cfg);
        IDictionary<string, IClusterWorker<T, TR>> Workers { get; }
    }

    public class WorkerDiscovered<T, TR> : IRxn
    {
        public IClusterWorker<T, TR> Worker { get; set; }
    }

}