using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.Tests
{
    /// <summary>
    /// How the initial status push is split before it goes on the wire.
    ///
    /// <para>It used to be one message carrying every tenant and every system, built fresh on
    /// every connect. A portal watching a few machines never notices. A fleet does: each of a
    /// hundred workers connects and is handed a description of the other ninety-nine, so the cost
    /// is quadratic in fleet size and the message eventually crosses the hub's limit. The server
    /// then closes the connection to report the limit, the client reconnects, and the identical
    /// message is rebuilt - workers that never finish registering, and a log showing nothing but
    /// reconnects.</para>
    ///
    /// <para>Measurement is injected, so these describe the split itself and never the
    /// serializer: a byte here is whatever the fake says it is.</para>
    /// </summary>
    [TestClass]
    [TestCategory("Internal - SignalR transport")]
    public class StatusPagingBehaviour
    {
        /// <summary>A system of a stated size. Size is the only thing paging can see.</summary>
        private sealed class Sized
        {
            public string Name;
            public int Bytes;
            public override string ToString() => Name;
        }

        private static readonly Func<object, int> Measure = o => ((Sized)o).Bytes;

        private static EventsHub.TenantStatus Tenant(string name, params Sized[] systems) =>
            new EventsHub.TenantStatus { Tenant = name, Systems = systems.Cast<object>().ToArray() };

        private static Sized[] Fleet(int count, int bytesEach) =>
            Enumerable.Range(0, count).Select(i => new Sized { Name = "worker-" + i, Bytes = bytesEach }).ToArray();

        /// <summary>Every system across every page, in the order a client would receive them.</summary>
        private static List<string> Delivered(IEnumerable<EventsHub.TenantStatus[]> pages) =>
            pages.SelectMany(p => p).SelectMany(t => t.Systems).Select(s => ((Sized)s).Name).ToList();

        [TestMethod]
        public void A_status_that_already_fits_is_still_one_message()
        {
            // The case every existing deployment is in. Paging must not change what they send,
            // or this fix trades a fleet-scale failure for a portal-scale one.
            var pages = EventsHub.PageStatus(
                new[] { Tenant("acme", Fleet(3, 100)) }, Measure, 64 * 1024);

            pages.Should().HaveCount(1, "nothing here comes close to the budget");
            pages[0].Should().HaveCount(1);
            pages[0][0].Tenant.Should().Be("acme");
            pages[0][0].Systems.Should().HaveCount(3);
        }

        [TestMethod]
        public void A_fleet_in_one_tenant_is_split_between_its_systems()
        {
            // The shape that actually breaks: a hundred workers all under one tenant. Splitting
            // only between tenants would return a single page and fix nothing.
            var pages = EventsHub.PageStatus(
                new[] { Tenant("fleet", Fleet(100, 4096)) }, Measure, 64 * 1024);

            pages.Count.Should().BeGreaterThan(1, "one tenant holding the whole fleet still has to split");
            Delivered(pages).Should().HaveCount(100, "every worker reaches the client");
            pages.Should().OnlyContain(p => p.All(t => t.Tenant == "fleet"),
                "each piece has to name its tenant or the client cannot merge them");
        }

        [TestMethod]
        public void No_page_exceeds_the_budget()
        {
            var pages = EventsHub.PageStatus(
                new[]
                {
                    Tenant("a", Fleet(40, 3000)),
                    Tenant("b", Fleet(25, 7000)),
                    Tenant("c", Fleet(1, 500))
                },
                Measure, 32 * 1024);

            foreach (var page in pages)
            {
                var bytes = page.Sum(t => t.Systems.Sum(Measure));
                bytes.Should().BeLessOrEqualTo(32 * 1024,
                    "a page over the budget is the message that gets refused");
            }
        }

        [TestMethod]
        public void Nothing_is_lost_and_nothing_is_duplicated()
        {
            // A split that drops a system turns a delivery failure into a portal quietly missing
            // a worker, which is worse - the screen looks healthy and the fleet is short.
            var expected = new List<string>();
            var tenants = new List<EventsHub.TenantStatus>();
            foreach (var name in new[] { "a", "b", "c" })
            {
                var systems = Enumerable.Range(0, 30)
                    .Select(i => new Sized { Name = name + "-" + i, Bytes = 1000 + i * 37 })
                    .ToArray();
                expected.AddRange(systems.Select(x => x.Name));
                tenants.Add(Tenant(name, systems));
            }

            var delivered = Delivered(EventsHub.PageStatus(tenants, Measure, 8 * 1024));

            string.Join(",", delivered).Should().Be(string.Join(",", expected),
                "paging changes how the status is carried, never what it says");
        }

        [TestMethod]
        public void A_system_larger_than_the_whole_budget_travels_alone()
        {
            // It cannot be made to fit, and there is nothing below a system to split. Dropping it
            // loses a worker silently; refusing to close the page loops forever. It goes on its
            // own page and the hub's own 4 MB limit remains the real backstop.
            var systems = new[]
            {
                new Sized { Name = "small-before", Bytes = 100 },
                new Sized { Name = "enormous",     Bytes = 500_000 },
                new Sized { Name = "small-after",  Bytes = 100 }
            };

            var pages = EventsHub.PageStatus(new[] { Tenant("t", systems) }, Measure, 8 * 1024);

            Delivered(pages).Should().Equal(new List<string> { "small-before", "enormous", "small-after" });

            var enormousPage = pages.Single(p => p.SelectMany(t => t.Systems).Any(s => ((Sized)s).Name == "enormous"));
            enormousPage.SelectMany(t => t.Systems).Should().HaveCount(1,
                "the oversized one must not drag a neighbour over the limit with it");
        }

        [TestMethod]
        public void An_empty_status_still_sends_one_page()
        {
            // The portal replaces its list on page 1. Sending nothing would leave the last fleet
            // on screen after it was torn down, which reads as a cluster that never stopped.
            var empty = EventsHub.PageStatus(new EventsHub.TenantStatus[0], Measure, 1024);
            empty.Should().HaveCount(1);
            empty[0].Should().BeEmpty("an empty page is what clears the portal's list");

            EventsHub.PageStatus(null, Measure, 1024).Should().HaveCount(1);
        }

        [TestMethod]
        public void A_tenant_with_no_systems_is_carried_not_dropped()
        {
            // It is how the portal learns a tenant exists with nothing reporting under it.
            var pages = EventsHub.PageStatus(
                new[] { Tenant("empty"), Tenant("busy", Fleet(2, 100)) }, Measure, 64 * 1024);

            pages.SelectMany(p => p).Select(t => t.Tenant).Should().Contain("empty");
        }

        /// <summary>Captures what actually reaches the wire, argument counts included.</summary>
        private sealed class RecordingProxy : IClientProxy
        {
            public readonly List<object[]> Sends = new List<object[]>();

            public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken = default)
            {
                Sends.Add(args);
                return Task.CompletedTask;
            }
        }

        [TestMethod]
        public void Every_page_is_sent_as_exactly_one_argument()
        {
            // The regression this exists for. The page marker was briefly a SECOND argument,
            // which reads as harmless: the JavaScript client ignores arguments its handler does
            // not declare, so the portal was unaffected and the change looked complete. The .NET
            // client does not - it binds on the registered handler's parameter count and DROPS
            // an invocation carrying more. Every worker uses the .NET client, so the effect was
            // to cut the whole fleet off from status while the browser kept working perfectly.
            var proxy = new RecordingProxy();
            var pages = EventsHub.PageStatus(new[] { Tenant("fleet", Fleet(100, 4096)) }, Measure, 64 * 1024);

            EventsHub.SendStatusPagesAsync(proxy, pages).GetAwaiter().GetResult();

            proxy.Sends.Should().HaveCount(pages.Count, "one send per page");
            proxy.Sends.Should().OnlyContain(args => args.Length == 1,
                "a second argument is silently dropped by the .NET client, along with the payload");
        }

        [TestMethod]
        public void Each_page_says_which_one_it_is()
        {
            // The client needs it to know whether to replace what it has or add to it, and it can
            // only learn that from the payload - see above for why not from a second argument.
            var proxy = new RecordingProxy();
            var pages = EventsHub.PageStatus(new[] { Tenant("fleet", Fleet(100, 4096)) }, Measure, 64 * 1024);

            EventsHub.SendStatusPagesAsync(proxy, pages).GetAwaiter().GetResult();

            for (var i = 0; i < proxy.Sends.Count; i++)
            {
                var sent = (EventsHub.TenantStatus[])proxy.Sends[i][0];
                sent.Should().OnlyContain(t => t.Page == i + 1 && t.Of == pages.Count,
                    "page " + (i + 1) + " of " + pages.Count + " has to say so on every piece it carries");
            }

            proxy.Sends.Select(s => ((EventsHub.TenantStatus[])s[0])[0].Page)
                .Should().Equal(Enumerable.Range(1, pages.Count),
                    "pages arrive in order, and page 1 is the one that means replace");
        }

        [TestMethod]
        public void A_single_page_is_indistinguishable_from_never_having_been_paged()
        {
            // Every deployment small enough not to need this must be completely unaffected.
            var proxy = new RecordingProxy();
            var pages = EventsHub.PageStatus(new[] { Tenant("acme", Fleet(3, 100)) }, Measure, 64 * 1024);

            EventsHub.SendStatusPagesAsync(proxy, pages).GetAwaiter().GetResult();

            proxy.Sends.Should().HaveCount(1);
            proxy.Sends[0].Length.Should().Be(1);
            ((EventsHub.TenantStatus[])proxy.Sends[0][0]).Should().OnlyContain(t => t.Page == 1 && t.Of == 1);
        }

        [TestMethod]
        public void The_page_count_stays_sane_at_fleet_scale()
        {
            // 600 workers is the scale-set ceiling, so this is the largest fleet that can exist.
            // The guard is against a pathological split - a page per system would mean 600
            // round trips on every single connect.
            var pages = EventsHub.PageStatus(
                new[] { Tenant("fleet", Fleet(600, 2048)) }, Measure, EventsHub.StatusPageBudgetBytes);

            Delivered(pages).Should().HaveCount(600);
            pages.Count.Should().BeLessThan(20, "pages should hold as much as the budget allows");
            pages.Count.Should().BeGreaterThan(1);
        }
    }
}
