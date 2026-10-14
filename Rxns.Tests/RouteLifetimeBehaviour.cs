using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Disposables;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Rxns.Interfaces;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Redis;

namespace Rxns.Tests
{
    /// <summary>
    /// What may remove a worker's route from the arena's dispatch table.
    ///
    /// <para>Answer: evidence, and only evidence. A heartbeat registers a route; an explicit
    /// WorkerRouteRemoved takes it away. Silence does neither - it is not information about the
    /// worker, it is information about the channel between them.</para>
    ///
    /// <para>This hub used to disagree. It evicted any route whose heartbeat had not been seen
    /// inside a deadline, synthesising the disconnect signal that Redis - unlike SignalR, which
    /// gets a real OnDisconnectedAsync - does not provide. Workers publish every 5s, so the
    /// deadline looked generous. Measured delivery was not: heartbeats ARRIVED at 22-25s with a
    /// 374s tail, because the arena's stream consumer was behind, and the whole fleet was
    /// therefore evicted over and over - 151 times in a ten-device run, 392 in a hundred-device
    /// one. Routes vanished, dispatches could not land, and three rungs finished partial with no
    /// error anywhere: 26 of 100, then 4 of 10, then 5 of 10. They read as hangs. They were not;
    /// the work never arrived.</para>
    ///
    /// <para>Worse, it fed itself: an eviction published an event onto the very stream whose
    /// backlog caused the eviction. Scaling the deadline with fleet size - the obvious fix - was
    /// measured and found to be the wrong shape entirely, since lag was p50 25.6s at SIX workers
    /// against 22.2s at 101. The delay never tracked fleet size.</para>
    ///
    /// <para>A genuinely dead worker does now keep its route. That is deliberate: a dispatch that
    /// does not come back is reported by its own timeout (StartUnitTest.TimeoutSeconds), which is
    /// evidence about that dispatch, rather than by a table guessing at liveness on a clock.</para>
    ///
    /// <para>No Redis is needed here. Everything under test is the in-memory route map and the two
    /// local-bus subscriptions that feed it; the hub's multiplexer is built with
    /// AbortOnConnectFail=false and so tolerates an address with nothing behind it.</para>
    /// </summary>
    [TestClass]
    [TestCategory("Internal - fleet routing")]
    public class RouteLifetimeBehaviour
    {
        /// <summary>As a caller spells it. Case is not significant to a route.</summary>
        private const string Route = @"acme\W1";

        /// <summary>
        /// As the hub stores it. AsRootRoute normalises to tenant and system, lower-cased, so the
        /// dispatch table is keyed by the normalised form and must be read back that way.
        /// </summary>
        private const string StoredAs = @"acme\w1";

        private Subject<IRxn> _bus;
        private RedisEventHub _hub;

        [TestInitialize]
        public void Setup()
        {
            _bus = new Subject<IRxn>();

            var rxnManager = Substitute.For<IRxnManager<IRxn>>();
            rxnManager.CreateSubscription<WorkerHeartbeat>().Returns(_bus.OfType<WorkerHeartbeat>());
            rxnManager.CreateSubscription<WorkerRouteRemoved>().Returns(_bus.OfType<WorkerRouteRemoved>());
            rxnManager.Publish(Arg.Any<IRxn>()).Returns(Observable.Return(Unit.Default));

            _hub = new RedisEventHub(
                // Nothing listens here. See the class note: the ctor does not require a live server.
                "127.0.0.1:65000,abortConnect=false,connectTimeout=50,connectRetry=0,syncTimeout=50",
                Substitute.For<IAppStatusStore>(),
                Substitute.For<IAppCmdManager>(),
                rxnManager,
                Substitute.For<IResolveTypes>(),
                "routelifetime-test-stream",
                "routelifetime-test-group");
        }

        [TestCleanup]
        public void Teardown()
        {
            _hub?.Dispose();
            _bus?.Dispose();
        }

        private void Heartbeat(string route = Route, string clientId = "client-1")
        {
            _bus.OnNext(new WorkerHeartbeat { Route = route, ClientId = clientId });
        }

        [TestMethod]
        public void A_worker_is_routable_from_its_first_heartbeat()
        {
            // Registration and liveness ride the same event on purpose - a separate "I'm joining"
            // was racy at boot, because a worker could send it before its publish chain was warm.
            _hub.HasRoute(Route).Should().BeFalse("nothing has been heard from this worker yet");

            Heartbeat();

            _hub.HasRoute(Route).Should().BeTrue("the first heartbeat is what registers a route");
            _hub.Routes[StoredAs].Should().Be("client-1", "dispatch needs the clientId to address the worker");
        }

        [TestMethod]
        public void Silence_does_not_cost_a_worker_its_route()
        {
            Heartbeat();

            var lifecycle = new List<ClientLifecycleEvent>();
            using (_hub.ClientLifecycle.Subscribe(lifecycle.Add))
            {
                // Say nothing at all. There is no clock in this hub that removes a route, so no
                // amount of waiting would change the answer - the test is fast because the
                // eviction timer is gone, not because it is being lenient about how long it
                // stays quiet. Nothing_in_the_hub_evicts_a_route_on_a_timer is what keeps the
                // timer from coming back and making this test vacuous.
                _hub.HasRoute(Route).Should().BeTrue("a quiet worker is not a departed one");
            }

            lifecycle.Should().BeEmpty("nothing happened, so there is nothing to report");
            _hub.Routes.Keys.Should().Contain(StoredAs, "the route outlives the silence");
        }

        [TestMethod]
        public void A_worker_that_says_it_is_leaving_loses_its_route()
        {
            Heartbeat();

            var lifecycle = new List<ClientLifecycleEvent>();
            using (_hub.ClientLifecycle.Subscribe(lifecycle.Add))
            {
                _bus.OnNext(new WorkerRouteRemoved { Route = Route });
            }

            _hub.HasRoute(Route).Should().BeFalse("an explicit removal is evidence, and is acted on");
            lifecycle.Should().ContainSingle(e => e.Connected == false,
                "a route lost for a known reason is worth announcing");
        }

        [TestMethod]
        public void A_respawned_worker_reclaims_its_route_under_its_new_identity()
        {
            // The case that makes never-expiring routes safe. A worker process can die and come
            // back on the same route with a new stream consumer id; without this the hub would
            // keep addressing the dead consumer's entries, which are silently dropped.
            Heartbeat(clientId: "client-before-crash");
            _hub.Routes[StoredAs].Should().Be("client-before-crash");

            Heartbeat(clientId: "client-after-respawn");

            _hub.Routes[StoredAs].Should().Be("client-after-respawn",
                "the newest heartbeat is the freshest evidence of where this worker actually is");
            _hub.Routes.Should().HaveCount(1, "it is the same worker on the same route, not a second one");
        }

        [TestMethod]
        public void Nothing_in_the_hub_evicts_a_route_on_a_timer()
        {
            // The guard. The tests above would pass just as happily with a 20-second sweep
            // running, since none of them wait that long - so this asserts the absence directly,
            // and is the thing that fails if a deadline is ever reintroduced.
            //
            // Structural, and deliberately so: the cost of reintroducing timer eviction was three
            // partial rungs that reported no error at all, and that is worth a brittle test.
            typeof(RedisEventHub).GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name)
                .Should().NotContain(n => n.IndexOf("Sweep", StringComparison.OrdinalIgnoreCase) >= 0
                                       || n.IndexOf("Evict", StringComparison.OrdinalIgnoreCase) >= 0
                                       || n.IndexOf("HeartbeatTimeout", StringComparison.OrdinalIgnoreCase) >= 0,
                    "a route is removed on evidence, never on a clock");

            var held = (CompositeDisposable)typeof(RedisEventHub)
                .GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_hub);

            held.Count.Should().Be(2,
                "the hub subscribes to exactly two things - heartbeats and explicit removals - and schedules nothing");
        }
    }
}
