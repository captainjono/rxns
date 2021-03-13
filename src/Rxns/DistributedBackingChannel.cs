using System;
using System.Linq;
using System.Reactive.Linq;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// Creates a new backing channel, where are Rxns are published locally, 
    /// with cetertain Rxns repeated centrally
    /// </summary>
    public static class DistributedBackingChannel
    {
        public static Func<IRxnLifecycle, IRxnLifecycle> For(params Type[] IRxnsToRepeat)
        {
            return lifecycle => lifecycle
                .CreatesOncePerApp<IRxnManager<IRxn>>(c => new RxnManager<IRxn>(new LocalBackingChannel<IRxn>()), named:"local")
                .CreatesOncePerApp<TaggedServiceRxnManagerRegistry>()
                .CreatesOncePerApp<IRxnManager<IRxn>>(cc =>
                {
                    var backingChannel = new AppStatusBackingChannel(cc.Resolve<IAppStatusServiceClient>());
                    var appStatusManager = new RxnManager<IRxn>(backingChannel);
                    backingChannel.ReportsOn(appStatusManager).DisposedBy(appStatusManager);

                    return appStatusManager;
                }, false, "centralReliable", "central")
                .CreatesOncePerApp(cc =>
                {
                    var registry = cc.Resolve<IRxnManagerRegistry>();
                    var router = new RoutableBackingChannel<IRxn>(registry);
                    var i = 0;

                    foreach (var route in IRxnsToRepeat.Select(type => RxnRouteCfg.OnReactionTo(type).PublishTo<IRxn>(e => registry.RxnsCentral.Publish(e).Until()).AndTo(e => registry.RxnsLocal.Publish(e).Until()))
                        .Concat(new[] { RxnRouteCfg.OnReaction().PublishTo<IRxn>(e => registry.RxnsLocal.Publish(e).Until()) }).ToArray())

                        router.ConfigureWith($"appStatus{i++}", route);
                    
                    return new RxnManager<IRxn>(router);
                });
        }
    }
}
