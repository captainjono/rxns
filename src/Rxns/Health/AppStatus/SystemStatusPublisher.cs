using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Hosting.Updates;
using Rxns.Logging;
using Rxns.Interfaces;

namespace Rxns.Health
{
    public class SystemStatusPublisher : ReportsStatus, IRxnProcessor<SystemStatusEvent>, IRxnProcessor<SystemStatusMetaEvent>
    {
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IAppUpdateManager _updates;
        private readonly Dictionary<string, SystemStatusMetaEvent> _meta = new Dictionary<string, SystemStatusMetaEvent>();

        public SystemStatusPublisher(IAppStatusServiceClient appStatus, IAppUpdateManager updates)
        {
            _appStatus = appStatus;
            _updates = updates;
        }

        /// <summary>
        /// todo: update method to listen for the command response and then send back to the server via the EventsService/ICommandService
        /// </summary>
        /// <param name="cmd"></param>
        public virtual IObservable<IRxn> Process(SystemStatusEvent status)
        {
            return Rxn.Create(() =>
            {
                //dont publish status while doing system level operations
                if (_updates.SystemStatus.Value() != AppUpdateStatus.Idle) return null;

                OnVerbose("Publishing status to support service");

                var meta = _meta.Values.Where(m => m.Meta != null).Select(m => m.Meta());
                var finalMeta = meta.Any() ? meta.ToArray() : new object[] { };

                return _appStatus.PublishSystemStatus(status, finalMeta); //returns commands as responses
            }).SelectMany(r => r);

        }

        public IObservable<IRxn> Process(SystemStatusMetaEvent meta)
        {
            return Rxn.Create<IRxn>(() =>
            {
                OnVerbose("Received meta from {0}", meta.ReporterName);

                _meta.AddOrReplace(meta.Component, meta);
            });
        }
    }
}
