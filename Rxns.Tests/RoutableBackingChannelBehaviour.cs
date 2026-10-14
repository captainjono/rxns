using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns;
using Rxns.Interfaces;

namespace Rxns.Tests
{
    /// <summary>
    /// What the router does with an event, and when it does nothing at all.
    ///
    /// <para>This is the channel every distributed host publishes through: a typed route sends an
    /// event to the central manager (which ships it to the other process) and to the local one; an
    /// untyped event goes local only. Under the Redis transport, worker results reach the arena
    /// through exactly this path and nothing else - so a router that silently swallows an event
    /// presents as a worker that ran the work and reported nothing, with no error anywhere.</para>
    /// </summary>
    [TestClass]
    [TestCategory("Internal - routing")]
    public class RoutableBackingChannelBehaviour
    {
        private class Routed : IRxn { }
        private class NotRouted : IRxn { }

        /// <summary>A manager that only records, so a route's effect is observable.</summary>
        private sealed class Recording : IRxnManager<IRxn>
        {
            public readonly List<IRxn> Published = new List<IRxn>();

            public IObservable<Unit> Publish(IRxn message)
            {
                Published.Add(message);
                return Observable.Return(Unit.Default);
            }

            public IObservable<IRxn> CreateSubscription() => Observable.Never<IRxn>();
            public IObservable<T> CreateSubscription<T>() => Observable.Never<T>();
            public IObservable<Unit> Activate() => Observable.Return(Unit.Default);
            public IObservable<Unit> Deactivate() => Observable.Return(Unit.Default);
            public void Dispose() { }
        }

        private sealed class Registry : IRxnManagerRegistry
        {
            public IRxnManager<IRxn> RxnsLocal { get; set; }
            public IRxnManager<IRxn> RxnsCentralReliable { get; set; }
            public IRxnManager<IRxn> RxnsCentral { get; set; }
            public IDictionary<string, IRxnManager<IRxn>> ClientRoutes { get; set; }
        }

        private static RoutableBackingChannel<IRxn> Wire(out Recording central, out Recording local)
        {
            var c = new Recording();
            var l = new Recording();
            central = c;
            local = l;

            var registry = new Registry { RxnsCentral = c, RxnsLocal = l, RxnsCentralReliable = c };
            var router = new RoutableBackingChannel<IRxn>(registry);

            router.ConfigureWith("typed", RxnRouteCfg.OnReactionTo(typeof(Routed))
                .PublishTo<IRxn>(e => c.Publish(e).Until())
                .AndTo(e => l.Publish(e).Until()));

            router.ConfigureWith("catchAll", new EventPublisherBuilder(o => !(o is Routed))
                .PublishTo<IRxn>(e => l.Publish(e).Until()));

            return router;
        }

        [TestMethod]
        public void A_typed_event_reaches_central_and_local()
        {
            var router = Wire(out var central, out var local);
            router.Setup();

            router.Publish(new Routed());

            central.Published.Should().HaveCount(1, "central is what carries it to the other process");
            local.Published.Should().HaveCount(1);
        }

        [TestMethod]
        public void An_untyped_event_stays_local()
        {
            var router = Wire(out var central, out var local);
            router.Setup();

            router.Publish(new NotRouted());

            central.Published.Should().BeEmpty("only declared types cross the process boundary");
            local.Published.Should().HaveCount(1);
        }

        [TestMethod]
        public void Publishing_before_setup_loses_the_event_silently()
        {
            // The shape of the bug this suite was written for. The router's pipeline is a plain
            // Subject, wired only by Setup(); publishing into it beforehand drops the event with
            // no route warning, no error and no log line - the event simply never existed.
            //
            // Pinned rather than fixed here because callers rely on Setup() being the activation
            // point. What this does is make the window explicit, so a caller that publishes
            // outside it is understood as a caller bug rather than a mystery.
            var router = Wire(out var central, out var local);

            router.Publish(new Routed());

            central.Published.Should().BeEmpty("nothing is subscribed to the pipeline yet");
            local.Published.Should().BeEmpty();

            // And once set up, the same publish lands - so the loss is purely about ordering.
            router.Setup();
            router.Publish(new Routed());
            central.Published.Should().HaveCount(1);
        }

        [TestMethod]
        public void Routing_stops_when_the_setup_subscription_is_disposed()
        {
            // Setup() returns a subscription whose disposal tears the routing pipeline down again
            // - and the teardown is silent, exactly like the pre-Setup window above. A host whose
            // subscription ends for any reason therefore keeps accepting publishes and routing
            // none of them, which is indistinguishable from a quiet cluster.
            var router = Wire(out var central, out var local);

            var sub = router.Setup().Subscribe();
            router.Publish(new Routed());
            central.Published.Should().HaveCount(1, "routing works while the subscription is live");

            sub.Dispose();

            router.Publish(new Routed());
            central.Published.Should().HaveCount(1,
                "this is the silent failure: the publish is accepted and goes nowhere");
        }
    }
}
