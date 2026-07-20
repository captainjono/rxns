using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.Autofac
{
    public class AutofacAppContainer : AutofacTypeResolver, IAppContainer
    {
        public readonly IContainer Container;
        private readonly ReplaySubject<IReportStatus> _reporterCreated = new ReplaySubject<IReportStatus>(100);

        public IObservable<IReportStatus> ReporterCreated => _reporterCreated;


        public AutofacAppContainer(IContainer container) : base(container)
        {
            Container = container;

            ContainerBuilder cb = new ContainerBuilder();
            cb.Register(_ => this).AsImplementedInterfaces().SingleInstance().ExternallyOwned();

            //create the event listener for any reporters so logging can occour
            var onReporterResolved = new OnTypeResolved<IReportStatus>();
            onReporterResolved.Resolved.Subscribe(_reporterCreated);
            cb.RegisterModule(onReporterResolved);
            cb.Update(Container);
            
            LogAllReporters();

            _reporterCreated.DisposedBy(this);

             //take over logging so ppl can stream the app logs via the container
             //maybe this is a violation of SoC?
             ReportStatus.StartupLogger.Dispose();
             ReportStatus.Log.Information.Subscribe(ReportInformation);
             ReportStatus.Log.Errors.Subscribe(ReportExceptions);

             foreach (var logger in container.Resolve<IRxnLogger[]>())
                 this.SubscribeAll(logger.Information, logger.Errors);
        }

        private void LogAllReporters()
        {
            ReporterCreated.Subscribe(this, reporter =>
            {
                if (reporter != this)
                    reporter.SubscribeAll(
                        info => this.ReportInformation.OnNext(info),
                        error => this.ReportExceptions.OnNext(error)
                    );
            })
            .DisposedBy(this);

            _reporterCreated.OnNext(this);
        }
    }
}
