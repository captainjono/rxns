using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Logging
{
    public class ErrorReporterCfg
    {
        public int ErrorReportHistoryLength { get; set; }
        public int MaxErrorsPerSecondBeforeFlood { get; set; }
    }

    public interface IErrorChannel
    {
        void Send(ErrorReport report);
    }

    public interface ISecurityMode
    {
        /// <summary>
        /// Tells the system to activate SSL or other transport level security
        /// </summary>
        bool IsSecure { get; }
    }

    public class INSECURE_SERVICE_DEBUG_ONLY_MODE : ISecurityMode
    {
        public bool IsSecure { get; }
    }
    
    public class ReporterErrorLogger : ReportStatusService
    {
        private readonly IEnumerable<IErrorChannel> _channels;
        private readonly CircularBuffer<LogMessage<string>> _messageBuffer;
        private readonly IRxnAppInfo _systemInfo;
        private readonly IScheduler _floodDetector;
        private readonly IAppContainer _container;
        private readonly ITenantCredentials _credentials;
        private readonly int _errorsPerSecThreadHold;
        private IScheduler _logScheduler;

        public ReporterErrorLogger(IAppContainer container, IEnumerable<IErrorChannel> channels, ErrorReporterCfg cfg, ITenantCredentials credentials, IRxnAppInfo systemInfo, ISecurityMode mode, IScheduler logScheduler = null, IScheduler floodDetector = null)
        {
            //disable all error logging in testing mode
            //if (!mode.IsSecure) return;

            _container = container;
            _credentials = credentials;
            _logScheduler = logScheduler ?? TaskPoolScheduler.Default;
            _systemInfo = systemInfo;
            _floodDetector = floodDetector;
            _channels = channels.ToArray();
            _errorsPerSecThreadHold = cfg.MaxErrorsPerSecondBeforeFlood;
            _messageBuffer = new CircularBuffer<LogMessage<string>>(cfg.ErrorReportHistoryLength < 1
                                ? 100
                                : cfg.ErrorReportHistoryLength);
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.Create(() =>
            {
                if (_channels.AnyItems())
                {
                    _container.Information.Subscribe(this, msg =>
                    {
                        _messageBuffer.Enqueue(msg);
                    }).DisposedBy(this);

                    _container.Errors.Subscribe(this, msg =>
                    {
                        _messageBuffer.Enqueue(new LogMessage<string>()
                        {
                            Level = msg.Level,
                            Message = msg.Message.ToString(),
                            Reporter = msg.Reporter,
                            Timestamp = msg.Timestamp
                        });
                    }).DisposedBy(this);

                    var errorStream = _container.Errors.ObserveOn(_logScheduler);

                    if (_errorsPerSecThreadHold > 0) //so we dont accidently break existing clients who dont need error flood detection
                        errorStream = errorStream
                            .Buffer(TimeSpan.FromSeconds(1), _floodDetector ?? Scheduler.Default) //lets counts the errors per second to ensure we aren't DoS'ing our channels
                            .Buffer(TimeSpan.FromSeconds(2), _floodDetector ?? Scheduler.Default) //sample for 3 seconds to see how severe the flooding may be
                            .Where(NotIndiciativeOfFlooding)
                            .SelectMany(errorSets => errorSets.SelectMany(errors => errors));

                    errorStream
                        .Subscribe(LogError)
                        .DisposedBy(this);
                }

                return CommandResult.Success();
            });
        }

        /// <summary>
        /// This method compares a set of errors against eachother to detect if we
        /// in a flood situation which can cause a DoS attack on our system. A flood is assumed
        /// if we breach our errorsPerSecThreadhold for each sample given
        /// </summary>
        /// <param name="errorsPerSec"></param>
        /// <returns></returns>
        private bool NotIndiciativeOfFlooding(IList<IList<LogMessage<Exception>>> errorsPerSec)
        {
            var overThreadhold = 0;

            foreach (var errorSet in errorsPerSec)
                if (errorSet.Count > _errorsPerSecThreadHold)
                    overThreadhold++;

            // Debug.WriteLine("{0} != {1}", overThreadhold, errorsPerSec.Count);
            return overThreadhold != errorsPerSec.Count;
        }

        /// <summary>
        /// Logs an error to any active error channels
        /// </summary>
        /// <param name="error"></param>
        public void LogError(LogMessage<Exception> error)
        {
            //be careful not to trigger an error reporting loop,
            //if something OnErrors(..) here, its going to get messy!
            try
            {
                if (error == null)
                    return;

                var report = new ErrorReport()
                {
                    Error = error,
                    Tenant = _credentials.Tenant,
                    System = _systemInfo.Name,
                    Timestamp = DateTime.Now,
                    History = _messageBuffer.Flush().ToArray()
                };

                foreach (var channel in _channels)
                {
                    try
                    {
                        channel.Send(report);
                    }
                    catch (Exception e)
                    {
                        OnWarning("Channel '{0}' failed while sending error: {1}\r\n{2}\r\n{3}", channel.GetType(), e.Message, e.StackTrace, report.Serialise());
                    }
                }
            }
            catch (Exception e)
            {
                OnWarning("This should not happen, cant compile error into report: {0}", e);
            }
        }
    }
}
