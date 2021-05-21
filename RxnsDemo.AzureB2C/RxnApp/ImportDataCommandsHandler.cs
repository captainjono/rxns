using System;
using System.Reactive.Linq;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;
using RxnsDemo.AzureB2C.RxnApps.Events;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class ImportDataCommandsHandler : ReportStatus, IRxnCfg, IRxnPublisher<IRxn>, IDomainCommandHandler<StartImportOfUsersIntoTenantCmd, string>
    {
        private Action<IRxn> _publish;


        public ImportDataCommandsHandler()
        {
        }

        public IObservable<DomainCommandResult<string>> Handle(StartImportOfUsersIntoTenantCmd cmd)
        {
            return Rxn.DfrCreate(() => Rxn.Create(() =>
            {
                OnInformation("<{0}> Processing import of {1} into '{2}''".FormatWith(cmd.Id, "users", cmd.Tenant));
                _publish(new ImportOfUsersIntoTenantQueuedEvent(cmd.Tenant, cmd.Id));
                _publish(new ImportOfUsersIntoTenantStartedEvent(cmd.Tenant, cmd.Id));

                cmd.Users.ToObservable().Buffer(500).Do(users =>
                {
                    foreach (var u in users)
                        _publish(u);
                }).Finally(() =>
                {
                    _publish(new ImportOfUsersIntoTenantStagedEvent(cmd.Tenant, cmd.Id) { ResultCount = cmd.Users.Length });
                })
                .Until();


                OnVerbose("<{0}> Finished processing import of {1} into '{2}''".FormatWith(cmd.Id, "users", cmd.Tenant));

                return cmd.ToSuccessResultWith(cmd.Id);
            }));
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }

        public string Reactor => "ImportUsers";
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; } = true;
        public RxnMode Mode { get; } = RxnMode.InProcess;
    }
}
