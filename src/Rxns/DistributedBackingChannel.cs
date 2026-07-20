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
            return BuildLifecycle(_ => IRxnsToRepeat ?? Array.Empty<Type>());
        }

        /// <summary>
        /// Like <see cref="For"/> but the central-routing list is read from the
        /// <see cref="EmitsRegistry"/> populated by <c>.Emits&lt;T&gt;()</c> /
        /// <c>.EmitsAnyIn&lt;T&gt;()</c> declarations on the lifecycle —
        /// resolved at DI build time so every emit registered before the
        /// router is built becomes a typed central route automatically.
        ///
        /// Use this instead of <see cref="For"/> when you want emit
        /// declarations to be the single source of truth for "what rides
        /// central" — no drift between Emits&lt;X&gt; and For(typeof(X)).
        /// </summary>
        public static Func<IRxnLifecycle, IRxnLifecycle> ForEmits()
        {
            return BuildLifecycle(cc => cc.Resolve<EmitsRegistry>().All());
        }

        private static Func<IRxnLifecycle, IRxnLifecycle> BuildLifecycle(Func<IResolveTypes, Type[]> resolveTyped)
        {
            return lifecycle => lifecycle
                .CreatesOncePerApp<IRxnManager<IRxn>>(c => new RxnManager<IRxn>(new LocalBackingChannel<IRxn>()), named: "local")
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
                    var typed = resolveTyped(cc) ?? Array.Empty<Type>();
                    var i = 0;

                    // Catch-all skips events already covered by a typed route.
                    // Without this guard a typed event hits BOTH the typed route
                    // (which fans to Central + Local) AND the fallthrough route
                    // (Local) — every AppResourceInfo arriving at the arena
                    // double-publishes to RxnsLocal, doubling subscriber work
                    // and producing two "matched route" log lines per event.
                    var catchAll = new EventPublisherBuilder(o => !typed.Any(t => t.IsAssignableFrom(o.GetType())))
                        .PublishTo<IRxn>(e => registry.RxnsLocal.Publish(e).Until());

                    foreach (var route in typed.Select(type => RxnRouteCfg.OnReactionTo(type).PublishTo<IRxn>(e => registry.RxnsCentral.Publish(e).Until()).AndTo(e => registry.RxnsLocal.Publish(e).Until()))
                        .Concat(new[] { catchAll }).ToArray())

                        router.ConfigureWith($"appStatus{i++}", route);

                    return new RxnManager<IRxn>(router);
                });
        }
    }
}
