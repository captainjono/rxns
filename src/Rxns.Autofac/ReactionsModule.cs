using System;
using System.Linq;
using Autofac;
using Autofac.Features.OwnedInstances;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    /// <summary>
    /// This module makes the container aware of the reactiosn framework, so u can register your
    /// interface implementations and things will just "work". Make sure you also register a listener for
    /// the IReportStatus interface so you can see the details of the process.
    /// </summary>
    public class ReactionsModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            cb.RegisterEvents<IRxn>();
            cb.RegisterType<AutofacRxnCreator>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<AutofacNamedEventManagerRegistry>().AsImplementedInterfaces().SingleInstance();

            cb.RegisterType<NeverAnyEventHistoryProvider>().AsImplementedInterfaces().SingleInstance().PreserveExistingDefaults();//noop rxn history provider placeholder

            //dont register reactor as IReactTo<> otherwise we will get recusive lookups
            cb.Register((c, p) => new Reactor<IRxn>(p.OfType<NamedParameter>().FirstOrDefault().Value.ToString())).As<IReactor<IRxn>>().As<IReportStatus>().InstancePerDependency();
            cb.Register<Func<string, IReactor<IRxn>>>(c =>
            {
                var cc = c.Resolve<IComponentContext>();
                return name =>
                {
                    //Only the reactor manager should clobber the reactor.
                    var selfManagedReactor = cc.Resolve<Owned<IReactor<IRxn>>>(new NamedParameter("name", name));
                    selfManagedReactor.Value.Disposes(selfManagedReactor);

                    return selfManagedReactor.Value;
                };
            }).As<Func<string, IReactor<IRxn>>>().SingleInstance();
            //want this to always exist, even longer then the container
            cb.RegisterType<ReactorManager>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterServiceCommands<ReactorManager>();

            base.Load(cb);
        }
    }
}
