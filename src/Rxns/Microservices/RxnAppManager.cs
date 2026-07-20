using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Microservices
{
    public interface IModule 
    {

    }

    public interface IAppContainer : IResolveTypes, IReportStatus
    {
    }

    

    /// <summary>
    /// This is an default implementation of bootstrapper for a "Rxn app"
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class RxnAppManager<T> : ReportsStatus
        where T : ReportsStatus, IAppContainer
    {
        public T Container { get; protected set; }

        public LogMessage<Exception> LastError { get; private set; }

        public List<IDisposable> ReportsStatusLoggingSubscription = new List<IDisposable>();

        protected RxnAppManager(Action<LogMessage<string>> information, Action<LogMessage<Exception>> errors, Action<object> ready = null, params IModule[] modules)
        {
            this.ReportExceptions(() =>
            {
                this.SubscribeAll(msg => information(Sanatise(msg)), e =>
                {
                    LastError = e;
                    errors(e);
                });

                //create the container
                Container = GetContainer(Setup().Concat(modules).ToArray());
                Container = Created(Container);

                //setup container logging before we build, so any no messages are missed
                this.ReportsOn(Container);

                if (ready != null)
                    ready(this);
            });
        }

        public abstract T GetContainer(IModule[] module);

        private LogMessage<string> Sanatise(LogMessage<string> msg)
        {
            msg.Message = msg.Message;

            return msg;
        }
        /// <summary>
        /// Here you define the modules that should be loaded into the container
        /// </summary>
        /// <returns></returns>
        public abstract IModule[] Setup();
        /// <summary>
        /// This is called immediately after the container is newed up
        /// </summary>
        public virtual T Created(T container) { return container; }

        public override void Dispose()
        {
            base.Dispose();

            Container.Dispose();
            ReportsStatusLoggingSubscription.DisposeAll();
        }
    }
}
