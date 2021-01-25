using System;
using Rxns.Health;

namespace Rxns.Hosting
{
    public class ReliableAppThatHeartbeatsEvery : IAppStatusServiceClientCfg
    {
        public bool EnableSupportHeartbeat => true;
        public TimeSpan HeartBeatInterval { get; }
        public TimeSpan SelfRecoveryTimeout { get; }

        public ReliableAppThatHeartbeatsEvery(TimeSpan interval)
        {
            HeartBeatInterval = interval;
            SelfRecoveryTimeout = interval.Add(TimeSpan.FromMinutes(interval.TotalMinutes * 10));
        }
    }
}
