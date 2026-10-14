using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.Tests
{
    /// <summary>
    /// The size ceiling a hub message is held to, and the fact that it is applied in every place
    /// SignalR checks it.
    ///
    /// <para>SignalR enforces the limit THREE times and raising it is not one setting.
    /// AddHubOptions covers MaximumReceiveMessageSize, which defaults to 32 KB; the other two live
    /// on the endpoint and default to 64 KB each. Raising only the first reads as done and simply
    /// moves the ceiling, which nothing in development gets near either way.</para>
    ///
    /// <para>These four were written after the three limits were already at 4 MB, so they pin no
    /// new behaviour. What they pin is the set staying a set: a fifth hub mapped without the call,
    /// or one of the values edited alone, restores a ceiling that only shows up at fleet scale and
    /// reads as reconnects when it does. No host, no socket.</para>
    /// </summary>
    [TestClass]
    [TestCategory("Internal - SignalR transport")]
    public class HubMessageSizeLimitBehaviour
    {
        private const long FourMb = 4L * 1024 * 1024;

        [TestMethod]
        public void The_limit_is_large_enough_for_a_batched_publish()
        {
            ConfigureAndStartAspnetCore.HubMessageSizeLimitBytes.Should().Be(FourMb,
                "a worker's registration and its first metrics batch travel as one message");
        }

        [TestMethod]
        public void Both_endpoint_buffers_are_raised_off_their_default()
        {
            var o = new HttpConnectionDispatcherOptions();

            // Measured on .NET 10, and stated here so a framework change to it is a failing
            // test rather than a silent shift in what this guards against. Note it is NOT the
            // 32768 that appears in SignalR's size error - that number is the hub limit, a
            // different check with a different default.
            o.ApplicationMaxBufferSize.Should().Be(64 * 1024, "the endpoint buffers are the ceiling behind the hub limit");
            o.TransportMaxBufferSize.Should().Be(64 * 1024);

            ConfigureAndStartAspnetCore.ApplyHubTransportLimits(o);

            o.ApplicationMaxBufferSize.Should().Be(FourMb, "the application buffer is checked before the hub sees the message");
            o.TransportMaxBufferSize.Should().Be(FourMb, "the transport buffer is checked before the application one");
        }

        [TestMethod]
        public void All_three_limits_agree()
        {
            // Three limits that do not match means the smallest silently wins, and which one that
            // is depends on the direction of travel - the hardest kind of mismatch to read back
            // off a failure.
            // HubOptions leaves MaximumReceiveMessageSize null until HubOptionsSetup supplies
            // the 32 KB default, so the raw object cannot be read for it - set it the way
            // AddHubOptions does and compare that.
            new HubOptions().MaximumReceiveMessageSize.Should().BeNull("the 32 KB default is applied by HubOptionsSetup, not by the options type");

            var hub = new HubOptions { MaximumReceiveMessageSize = ConfigureAndStartAspnetCore.HubMessageSizeLimitBytes };
            var endpoint = new HttpConnectionDispatcherOptions();
            ConfigureAndStartAspnetCore.ApplyHubTransportLimits(endpoint);

            hub.MaximumReceiveMessageSize.Should().Be(endpoint.ApplicationMaxBufferSize);
            endpoint.ApplicationMaxBufferSize.Should().Be(endpoint.TransportMaxBufferSize);
        }

        [TestMethod]
        public void Every_hub_that_is_mapped_gets_the_endpoint_limits()
        {
            // The regression this guards is not a wrong value - it is a hub mapped without the
            // call at all. A fifth hub added beside the four here inherits 32 KB, behaves
            // perfectly until something large crosses it, and nothing in review looks wrong.
            // Read off the source because the limits are only observable on a built endpoint,
            // which needs a running host to inspect.
            var source = File.ReadAllText(ConfigureSource());

            var mapped = Regex.Matches(source, @"MapHub<(\w+)>\s*\(\s*""[^""]*""\s*,\s*o\s*=>\s*\{(.*?)\n(\s*)\}\s*\)",
                RegexOptions.Singleline);

            mapped.Count.Should().BeGreaterThan(0, "the guard is worthless if it matches no hubs");

            foreach (Match m in mapped)
            {
                m.Groups[2].Value.Should().Contain("ApplyHubTransportLimits(o)",
                    "MapHub<" + m.Groups[1].Value + "> leaves its buffers at 32 KB");
            }
        }

        [TestMethod]
        public void The_limit_survives_being_resolved()
        {
            // The one that matters, and the one an AddHubOptions delegate cannot tell you.
            //
            // Assigning inside AddHubOptions only queues a delegate. What the hub actually
            // enforces is the RESOLVED HubOptions<THub>, built by running every registered
            // IConfigureOptions in registration order - so a later registration silently wins,
            // and the losing delegate still runs and still logs the value it assigned. The
            // arena's own startup diagnostic reports this resolved number for exactly that
            // reason; anything that reads it back as 32768 means the assignment did not stick.
            //
            // Driving the real registration rather than a copy of it: a copy would keep passing
            // while the thing it stands in for drifted.
            var services = new ServiceCollection();
            services.AddLogging();
            ConfigureAndStartAspnetCore.AddRxnsSignalR(services);

            using var provider = services.BuildServiceProvider();
            var resolved = provider.GetRequiredService<IOptions<HubOptions<EventsHub>>>().Value;

            resolved.MaximumReceiveMessageSize.Should().Be(FourMb,
                "this is the number the hub enforces - 32768 here means the 4 MB assignment was overwritten after it ran");
        }

        [TestMethod]
        public void A_hub_that_configures_nothing_still_gets_the_raised_floor()
        {
            // This asserted the opposite until the progress hub turned up sitting on the 32 KB
            // default with both its endpoint buffers already at 4 MB - two of three raised, which
            // is the same half-done state this whole area keeps producing. The hub could not be
            // fixed where it is mapped either: IAspnetCoreCfg offers no IServiceCollection hook,
            // so a host can reach the buffers at MapHub and never the receive limit.
            //
            // So the limit became a floor. A hub that configures nothing now inherits it, which
            // is the only version of this that a newly added hub cannot get wrong.
            var services = new ServiceCollection();
            services.AddLogging();
            ConfigureAndStartAspnetCore.AddRxnsSignalR(services);

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IOptions<HubOptions<SystemMetricsHub>>>().Value
                .MaximumReceiveMessageSize.Should().Be(FourMb,
                    "SystemMetricsHub configures no limit of its own and inherits the floor");

            provider.GetRequiredService<IOptions<HubOptions>>().Value
                .MaximumReceiveMessageSize.Should().Be(FourMb,
                    "the floor itself - every per-hub options copies from here");
        }

        [TestMethod]
        public void The_limit_survives_the_container_the_web_host_actually_uses()
        {
            // The host does not serve from the ServiceCollection. It calls
            // UseServiceProviderFactory(new AutofacServiceProviderFactory()), so every service the
            // hub is built from is resolved out of an Autofac container populated from those
            // descriptors - and the population is a vendored copy in this repo, not the upstream
            // package. A copy that handled the type-based registration but dropped the
            // instance-based one would leave HubOptionsSetup in place and the 4 MB delegate
            // behind, resolving to exactly the 32768 the arena's startup diagnostic reports.
            //
            // It does not: this passes. Which settles what that diagnostic means - it is reading a
            // different container from the one that serves connections, so its number says nothing
            // about what the hub enforces.
            var services = new ServiceCollection();
            services.AddLogging();
            ConfigureAndStartAspnetCore.AddRxnsSignalR(services);

            var builder = new ContainerBuilder();
            builder.Populate(services);
            using var container = builder.Build();

            var resolved = container.Resolve<IOptions<HubOptions<EventsHub>>>().Value;

            resolved.MaximumReceiveMessageSize.Should().Be(FourMb,
                "the Autofac container is what serves the hub, so this is the number it enforces");
        }

        [TestMethod]
        public void A_container_that_never_saw_the_registrations_reports_the_framework_default()
        {
            // The other half of the explanation, pinned so the reasoning is not just an assertion
            // in a commit message. Ask a container that was never populated from the SignalR
            // descriptors and it answers with an unconfigured HubOptions - null, which the arena's
            // diagnostic prints as -1, exactly as it does for the sibling hub that has no
            // AddHubOptions call at all.
            var bare = new ContainerBuilder();
            bare.Populate(new ServiceCollection().AddLogging());
            using var container = bare.Build();

            container.Resolve<IOptions<HubOptions<EventsHub>>>().Value
                .MaximumReceiveMessageSize.Should().BeNull(
                    "nothing configured it, so the value is absent rather than wrong");
        }

        [TestMethod]
        public void The_value_the_transport_reads_is_the_floor_even_for_an_unconfigured_hub()
        {
            // Everything above reads HubOptions. This reads what SignalR actually enforces, which
            // is not the same thing and is the distinction the arena's diagnostic gets wrong in
            // both directions.
            //
            // HubConnectionHandler<THub> resolves one number at construction: the per-hub value if
            // a user configured that hub, otherwise the global. So a hub with no AddHubOptions has
            // a null per-hub MaximumReceiveMessageSize - which a diagnostic prints as "unset", and
            // which the transport reads as the floor. theBFG's progress hub is exactly that hub,
            // and the startup line reporting -1 for it says nothing about what it enforces.
            //
            // AppStatusLogHub is rxns' own hub with no AddHubOptions call, so it stands in for it.
            // Read through the private field because the resolution rule lives in the handler and
            // UserHasSetValues, which drives it, is internal to the framework.
            var services = new ServiceCollection();
            services.AddLogging();
            ConfigureAndStartAspnetCore.AddRxnsSignalR(services);

            using var provider = services.BuildServiceProvider();
            var handler = provider.GetRequiredService<HubConnectionHandler<AppStatusLogHub>>();

            var field = typeof(HubConnectionHandler<AppStatusLogHub>)
                .GetField("_maximumMessageSize", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull(
                "the field this reads is framework-internal - if it is renamed this test has to be re-derived, not deleted");

            ((long?)field.GetValue(handler)).Should().Be(FourMb,
                "a hub that configures nothing still has to enforce the floor");
        }

        /// <summary>Walks up from the test binaries to the file in the checkout beside them.</summary>
        private static string ConfigureSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Rxns.WebApiNET5", "ConfigureAndStartAspnetCore.cs");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                "Could not find Rxns.WebApiNET5/ConfigureAndStartAspnetCore.cs above " + AppContext.BaseDirectory +
                " - the source guard cannot run, which is itself the failure.");
        }
    }
}
