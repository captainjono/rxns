using System;
using System.Linq;
using Rxns.Autofac;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Reliability;
using Rxns.Scheduling;

namespace Rxns.Health.AppStatus
{
    public class RxnsModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .EmitsAnyIn<IRxn>()
                .CreatesOncePerApp<NeverAnyEventHistoryProvider>(true)
                .CreatesOncePerApp<CrossPlatformOperatingSystemServices>()
                .CreatesOncePerRequestAs<IReactor<IRxn>, IReportStatus>((c, p) => new Reactor<IRxn>(p.FirstOrDefault().ToString()))
                //dont register reactor as IReactTo<> otherwise we will get recusive lookups
                .CreatesOncePerApp<Func<string, IReactor<IRxn>>>(c =>
                {
                    return name =>
                    {
                        //Only the reactor manager should clobber the reactor.
                        var selfManagedReactor = c.Resolve<IReactor<IRxn>>(new Tuple<string, object>("name", name));
                        //selfManagedReactor.Value.Disposes(selfManagedReactor);

                        return selfManagedReactor;
                    };
                })
                .CreatesOncePerApp<BasicDevice>()
                .CreatesOncePerApp<ReactorManager>()
                .CreatesOncePerApp(_ => new AllowAllReactions())
                .CreatesOncePerApp<RxnDebugLogger>()
                .CreatesOncePerApp<RxnManager<IRxn>>(true)
                .CreatesOncePerApp<LocalBackingChannel<IRxn>>(true)
                .CreatesOncePerApp<ResolverCommandFactory>()
                .RespondsToSvcCmds<StartReactor>()

                //order is important. start reactions before services
                .CreatesOncePerApp<PostBuildRxnServiceCreator>()
                .CreatesOncePerApp<PostBuildRxnCreator>()

                //register configuration providers
                .CreatesOncePerApp<PulseServiceTaskProvider>()

                .CreatesOncePerApp<ReliabilityManager>()
                .CreatesOncePerApp(() => new RetryMaxTimesReliabilityCfg(3), true)
                .CreatesOncePerApp<LocalRouteInfo>()
                .RespondsToSvcCmds<SendReactorOutOfProcess>()
                .CreatesOncePerApp<ServiceCommandExecutor>()
                
                //scaleout
                
                ;
        }
    }
}
