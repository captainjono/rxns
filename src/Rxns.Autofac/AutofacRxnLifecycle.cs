using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Autofac;
using Autofac.Core;
using Rxns;
using Rxns.Autofac;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;
using Rxns.Scheduling;
using StringExtensions = System.StringExtensions;

namespace Autofac
{
    public static class AutofacRxnsExt
    {
        public static IRxnDef ToRxnDef(this ContainerBuilder cb)
        {
            return new AutofacRxnDef(cb);
        }
        public static IRxnApp ToRxns(this ContainerBuilder cb)
        {
            return ToRxnsSupporting(cb);
        }

        public static IRxnApp ToRxnsSupporting(this ContainerBuilder cb, IObservable<IDisposable> rxn, string[] args, Action<IRxnLifecycle> configureWith = null)
        {
            var def = cb.ToRxnDef();

            def?.UpdateWith(configureWith);

            return new RxnApp(rxn, def, new RxnAppFactory());
        }

        public static IRxnApp ToRxnsSupporting(this ContainerBuilder cb, Action<IRxnLifecycle> configureWith = null)
        {
            var def = cb.ToRxnDef();

            def?.UpdateWith(configureWith);

            return new RxnApp(def, new RxnAppFactory());
        }

        //need way to convert irxnlifecycle to an IRxnApp

        //public static IRxnDef ToRxns(this IContainer cont)
        //{
        //    return new AutofacRxnDef(cont);
        //}
        
        //public static void Update(this AutofacRxnDef def, AutofacRxnDef other)
        //{
        //    other.WrappedDef.Update(def.WrappedContainer);
        //}

        public static IRxnApp ToRxns(this Action<IRxnLifecycle> configurator)
        {
            return new ContainerBuilder().ToRxnsSupporting(configurator);
        }
        public static IRxnApp ToRxns<T>(this Action<IRxnLifecycle> configurator, IObservable<T> rxn, string[] args) where T : IDisposable
        {
            return new ContainerBuilder().ToRxnsSupporting(Rxn.DfrCreate(() => rxn.Until()), args, configurator);
        }
    }

    public class AutofacRxnDef : IRxnDef
    {
        //todo: remove this from rxndef, we can compose the container in last?
        //use ms di extesions instead, same as aspnet
        public ContainerBuilder WrappedDef; 
      //  public IContainer WrappedContainer;
        public IAppContainer Container { get; private set; }


        public AutofacRxnDef()
        {

        }
        
        public AutofacRxnDef(ContainerBuilder cb)
        {
            WrappedDef = cb;
        }
        public IRxnLifecycle Configure()
        {
            return new AutofacRxnLifecycle(WrappedDef);
        }


        public IRxnDef UpdateWith(Action<IRxnLifecycle> lifecycle)
        {
            //hack to fix multiple builds of a container. need to fix
            if (WasBuilt())
            {
                WrappedDef = new ContainerBuilder();
            }

            var lifeCycle = Configure();
            lifecycle(lifeCycle);

            return this;
        }

        private bool WasBuilt()
        {
            if (WrappedDef == null)
                return false;

            FieldInfo type = typeof(ContainerBuilder).GetField("_wasBuilt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (bool) type.GetValue(WrappedDef);
        }

        public void Build(IAppContainer updateExisting = null)
        {
            if (updateExisting != null)
            {
                WrappedDef.Update(((AutofacAppContainer) updateExisting).Container);
                Container = updateExisting;
            }
            else
                Container = new AutofacAppContainer(WrappedDef.Build());

            WrappedDef = new ContainerBuilder();
        }

        public void Use(IAppContainer container)
        {
            Container = container;
        }
    }

    public class AutofacRxnLifecycle : IRxnLifecycle
    {
        public readonly ContainerBuilder _cb;

        public AutofacRxnLifecycle(ContainerBuilder cb)
        {
            _cb = cb;
        }
        // add support for named params in factoryfuncs
        // cb.Register((c, p) => new Reactor<IRxn>(p.OfType<NamedParameter>().FirstOrDefault().Value.ToString())).As<IReactor<IRxn>>().As<IReportStatus>().InstancePerDependency();

        public IRxnLifecycle CreatesOncePerAppNamed<T, TName>(string name = null, bool preserveExisting = false)
        {
            var reg = _cb.RegisterType<T>().AsImplementedInterfaces().AsSelf().SingleInstance();

            if (preserveExisting)
                reg.PreserveExistingDefaults();

            if (!StringExtensions.IsNullOrWhitespace(name))
                reg.Named<TName>(name ?? typeof(T).Name);

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestNamed<T, TName>(string name = null, bool preserveExisting = false)
        {
            var reg = _cb.RegisterType<T>().AsImplementedInterfaces().AsSelf().InstancePerDependency();

            if (preserveExisting)
                reg.PreserveExistingDefaults();

                reg.Named<TName>(name ?? typeof(T).Name);

            return this;
        }

        public IRxnLifecycle CreatesOncePerAppNamed<T, TName>(Func<T> factory, string name, bool preserveExisting = false)
        {
            var reg = _cb.Register(_ => factory()).SingleInstance();

            if (preserveExisting)
                reg.PreserveExistingDefaults();

                reg.Named<TName>(name ?? typeof(T).Name);

            return this;
        }

        public IRxnLifecycle CreatesOncePerApp<T>(bool preserveExisting = false)
        {
            var reg = _cb.RegisterType<T>().AsImplementedInterfaces().AsSelf().SingleInstance();

            if (preserveExisting)
                reg.PreserveExistingDefaults();

            return this;
        }

        public IRxnLifecycle CreatesOncePerAppAs<TService, TImplements>(bool preserveExisting = false)
        {
            if (!preserveExisting)
                _cb.RegisterType<TService>().As<TImplements>().AsSelf().SingleInstance();
            else
                _cb.RegisterType<TService>().As<TImplements>().AsSelf().SingleInstance().PreserveExistingDefaults();

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequest<T>()
        {
            _cb.RegisterType<T>().AsImplementedInterfaces().AsSelf().InstancePerDependency();
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestAs<T>(Func<IResolveTypes, IEnumerable<object>, T> factory)
        {
            _cb.Register<T>((c, p) => factory(new AutofacComponentContextResolver(c), p.OfType<NamedParameter>().Select(pm => pm.Value))).As<T>().InstancePerDependency();

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestAs<T, T2>(Func<IResolveTypes, IEnumerable<object>, T> factory)
        {
            _cb.Register<T>((c, p) => factory(new AutofacComponentContextResolver(c), p.OfType<NamedParameter>().Select(pm => pm.Value))).As<T>().As<T2>().InstancePerDependency();

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestAs<T, T2, T3>(Func<IResolveTypes, IEnumerable<object>, T> factory)
        {
            _cb.Register<T>((c, p) => factory(new AutofacComponentContextResolver(c), p.OfType<NamedParameter>().Select(pm => pm.Value))).As<T>().As<T2>().As<T3>().InstancePerDependency();

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequest<T>(Func<T> factory)
        {
            _cb.Register<T>(_ => factory()).AsImplementedInterfaces().AsSelf().InstancePerDependency();
            return this;

        }

        public IRxnLifecycle CreatesOncePerApp<T>(Func<T> factory, bool preserveExisting = false, string named = null)
        {
            var reg = _cb.Register<T>(_ => factory()).AsImplementedInterfaces().AsSelf().SingleInstance();

            if (preserveExisting)
                reg.PreserveExistingDefaults();

            if (!StringExtensions.IsNullOrWhitespace(named))
                reg.Named<T>(named);

            return this;
        }

        public IRxnLifecycle CreatesOncePerRequest<T>(Func<IResolveTypes, T> factory)
        {
            _cb.Register<T>(_ => factory(new AutofacComponentContextResolver(_.Resolve<IComponentContext>()))).AsImplementedInterfaces().AsSelf().InstancePerDependency();
            return this;
        }

        public IRxnLifecycle CreatesOncePerApp<T>(Func<IResolveTypes, T> factory, bool preserveExisting = false, params string[] named)
        {
            var reg = _cb.Register<T>(_ => factory(new AutofacComponentContextResolver(_.Resolve<IComponentContext>()))).AsImplementedInterfaces().AsSelf().SingleInstance();

            if(preserveExisting)
                reg.PreserveExistingDefaults();
            
            foreach(var name in named)
                reg.Named<T>(name);

            return this;
        }

        public IRxnLifecycle CreatesOncePerApp(Type type)
        {
            _cb.RegisterType(type).AsImplementedInterfaces().AsSelf().SingleInstance();
            return this;
        }

        public IRxnLifecycle CreateGenericOncePerAppAs(Type type, Type asT)
        {
            _cb.RegisterGeneric(type).As(asT).SingleInstance();
            return this;
        }

        public IRxnLifecycle RespondsToCmd<T>() where T : IDomainCommand
        {
            _cb.RegisterEvent<T>();
            _cb.RegisterType(typeof(T)).As<IServiceCommand>().Named<IServiceCommand>(typeof(T).FullName).InstancePerDependency();

            return this;
        }
        public IRxnLifecycle RunsTask<T>() where T : ITask
        {
            _cb.RegisterTasks<T>();
            
            return this;
        }


        public IRxnLifecycle RespondsToQry<T>() where T : IDomainQuery
        {
            _cb.RegisterEvent<T>();
            _cb.RegisterType(typeof(T)).As<IServiceCommand>().Named<IServiceCommand>(typeof(T).FullName).InstancePerDependency();
            return this;
        }

        public IRxnLifecycle RespondsToSvcCmds<T>() where T : IServiceCommand
        {
            _cb.RegisterServiceCommands<T>();
            return this;
        }

        public IRxnLifecycle Emits<T>() where T : IRxn
        {
            _cb.RegisterEvent<T>();
            return this;
        }

        public IRxnLifecycle EmitsAnyIn<T>() where T : IRxn
        {
            _cb.RegisterEvents<T>();
            return this;
        }

        public IRxnLifecycle Includes<T>() where T : IAppModule, new()
        {
            var other = new T();
            other.Load(this);

            return this;
        }

        public virtual IAppContainer Build()
        {
            var acnt = _cb.Build();
            var cnt = new AutofacAppContainer(acnt);
            
            var cb = new ContainerBuilder();
            cb.Register(_ => cnt).AsImplementedInterfaces().SingleInstance();

            cb.Update(acnt);

            return cnt;
        }
    }
}
