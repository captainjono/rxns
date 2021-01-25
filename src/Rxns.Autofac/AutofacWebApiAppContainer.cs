using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac;
using Rxns.Hosting;
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


        public AutofacAppContainer(IContainer container, bool startRxns = true) : base(container)
        {
            Container = container;

            ContainerBuilder cb = new ContainerBuilder();
            cb.Register(_ => this).AsImplementedInterfaces().SingleInstance().ExternallyOwned();

            //create the event listener for any reporters so logging can occour
            var onReporterResolved = new OnTypeResolved<IReportStatus>();
            onReporterResolved.Resolved.Subscribe(_reporterCreated);
            cb.RegisterModule(onReporterResolved);

            LogAllReporters();

            _reporterCreated.DisposedBy(this);

            if (!GeneralLogging.Log.ReportExceptions.HasObservers)
            {
                GeneralLogging.Log.Information.Subscribe(ReportInformation);
                GeneralLogging.Log.Errors.Subscribe(ReportExceptions);
            }

            var logger = container.Resolve<IRxnLogger>();
            this.SubscribeAll(logger.Information, logger.Errors);

            cb.Update(Container);
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
