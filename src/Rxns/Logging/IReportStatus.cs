using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rxns.Logging;

namespace Rxns.Interfaces
{

    public interface IRxnLogger
    {
        Action<LogMessage<string>> Information { get; }
        Action<LogMessage<Exception>> Errors { get; }
    }

    public class RxnDebugLogger : RxnLogger
    {
        public RxnDebugLogger() : base(i =>
        {
            if(Debugger.IsAttached)
                Debug.WriteLine(i);
            else
                Console.WriteLine(i);

        }, e =>
        {
            if (Debugger.IsAttached)
                Debug.WriteLine(e);
            else
                Console.WriteLine(e); 
        })
        {

        }
    }

    public class RxnLogger : IRxnLogger
    {
        public RxnLogger(Action<LogMessage<string>> information, Action<LogMessage<Exception>> errors)
        {
            Information = information;
            Errors = errors;
        }

        public Action<LogMessage<string>> Information { get; }
        public Action<LogMessage<Exception>> Errors { get; }
    }


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

        void OnInformation(string info, params object[] args);

        void OnWarning(string info, params object[] args);

        void OnVerbose(string info, params object[] args);
    }

}
