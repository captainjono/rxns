using System.Reactive.Subjects;
using Rxns.Interfaces;

namespace Rxns.Health
{
    public interface IReportHealth : IReporterName
    {
        ISubject<IHealthEvent> Pulse { get; }
        /// <summary>
        /// causes a pulse to occour in demand
        /// </summary>
        void Shock();
    }
}
