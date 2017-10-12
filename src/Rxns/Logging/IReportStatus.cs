using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Interfaces
{
    public interface IReporterName
    {
        string ReporterName { get; }
    }

    /// <summary>
    /// A univerisal mechansim to let other classes know about the internal state the class.
    /// This is unseful for classes which are resilliant to errors conditions, reset themselves,
    /// but still want to let other interested parties know that these errors have occoured
    /// </summary>
    public interface IReportStatus : IManageResources, IReporterName
    {
        /// <summary>
        /// A pipeline to report errors on
        /// </summary>
        IObservable<LogMessage<Exception>> Errors { get; }
        /// <summary>
        /// A pipeline to report general information on
        /// </summary>
        IObservable<LogMessage<string>> Information { get; }


        void OnError(Exception exception);

        void OnError(string exceptionMessage, params object[] args);

        void OnError(Exception innerException, string exceptionMessage, params object[] args);

        void OnInformation(string info, params object[] args);

        void OnWarning(string info, params object[] args);

        void OnVerbose(string info, params object[] args);
    }

}
