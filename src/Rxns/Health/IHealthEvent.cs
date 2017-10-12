using System;
using Rxns.Interfaces;

namespace Rxns.Health
{
    public interface IHealthEvent : IReporterName, IRxn
    {
        DateTime TimeCaptured { get; }
        IHealthEvent Tunnel(IReportHealth another);
    }
}
