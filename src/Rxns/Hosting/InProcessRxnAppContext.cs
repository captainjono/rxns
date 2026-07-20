using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using Rxns.DDD;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public class InProcessRxnAppContext : IRxnAppContext
    {
        private readonly List<IDisposable> _resources = new List<IDisposable>();
        private IRxnApp _app;
        public IRxnHostableApp App => Resolver.Resolve<IRxnHostableApp>();
        public IAppSetup Installer => App.Installer;
        public ICommandService CmdService => Resolver.Resolve<ICommandService>();
        public IAppCommandService AppCmdService => Resolver.Resolve<IAppCommandService>();
        public IRxnManager<IRxn> RxnManager => Resolver.Resolve<IRxnManager<IRxn>>();
        public IResolveTypes Resolver { get; set; }
        public string[] args { get; }
        public IObservable<IRxnAppContext> Start(bool shouldStartRxns = true, IAppContainer container = null)
        {
            if (container != null)
                Resolver = container;

            return this.ToObservable();
        }

        public void Terminate()
        {
            "Terminate not implemented for inprocess yet".LogDebug();
        }

        public IObservable<ProcessStatus> Status => new BehaviorSubject<ProcessStatus>(ProcessStatus.Active);

        public InProcessRxnAppContext(IRxnApp app, IResolveTypes container)
        {
            _app = app;
            
            Resolver = container;
        }

        public void Dispose()
        {
            _resources.DisposeAll();
            _resources.Clear();
        }

        public void OnDispose(IDisposable obj)
        {
            obj.DisposedBy(_resources);
        }
    }

    public class MicroAppContext : IRxnAppContext
    {
        private readonly List<IDisposable> _resources = new List<IDisposable>();
        private IMicroApp _app;
        public IRxnHostableApp App { get; }
        public IAppSetup Installer => null;
        public ICommandService CmdService => Resolver.Resolve<ICommandService>();
        public IAppCommandService AppCmdService => Resolver.Resolve<IAppCommandService>();
        public IRxnManager<IRxn> RxnManager => Resolver.Resolve<IRxnManager<IRxn>>();
        public IResolveTypes Resolver { get; set;  }
        public string[] args { get; }

        public IObservable<IRxnAppContext> Start(bool shouldStartRxns = true, IAppContainer container = null)
        {
            //if (args == null)
            //{
            //    "this could be an issue for multipe process running at once".LogDebug();
            //    return Rxn.Empty<IRxnAppContext>();
            //}

            return Rxn.Create<IRxnAppContext>(o =>
            {
                Resolver = container;
                o.OnNext(this);

                return this;
            });
        }

        public void Terminate()
        {
            "Terminate not implemented for inprocess yet".LogDebug();
        }

        public IObservable<ProcessStatus> Status => new BehaviorSubject<ProcessStatus>(ProcessStatus.Active);

        public MicroAppContext(IMicroApp app, IResolveTypes container)
        {
            _app = app;
            args = app.Args;
            Resolver = container;
        }

        public void Dispose()
        {
            _resources.DisposeAll();
            _resources.Clear();
        }

        public void OnDispose(IDisposable obj)
        {
            obj.DisposedBy(_resources);
        }
    }
}
