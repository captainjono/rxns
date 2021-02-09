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
    public class SystemStatusPublisher : ReportsStatus, IRxnProcessor<SystemStatusEvent>, IRxnProcessor<AppStatusInfoProviderEvent>
    {
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IAppUpdateManager _updates;
        private readonly Dictionary<string, AppStatusInfoProviderEvent> _meta = new Dictionary<string, AppStatusInfoProviderEvent>();

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

                var meta = _meta.Values.Where(m => m.Info != null).Select(m => m.Info());
                var finalMeta = meta.Any() ? meta.ToArray() : new object[] { };


                //need to advertise and update via the supervisor?
                //or i need to fix appstatus to look for main and only send updates to it?
                // -- i tried to fix appstatus, but i cant ref it cause its net461. need to convert to .net core? wasted my time before running .netduel?
                //

                //do i want to support diffrent reactors at differnet versions? not sure. dont think so. not worth the effort?
                //debug carefully once removing this
                //need to 
                //"REMOVE DEBUGGGG OVERIDE".LogDebug();

                //if (status.SystemName.EndsWith("[main]"))
                //{
                //    status.KeepUpToDate = true;
                //    status.SystemName = status.SystemName.Split('[')[0];
                //}

                return _appStatus.PublishSystemStatus(status, finalMeta); //returns commands as responses
            }).SelectMany(r => r);

        }

        public IObservable<IRxn> Process(AppStatusInfoProviderEvent infoProvider)
        {
            return Rxn.Create<IRxn>(() =>
            {
                OnVerbose("Received meta from {0}", infoProvider.ReporterName);

                _meta.AddOrReplace(infoProvider.Component, infoProvider);
            });
        }
    }
}
