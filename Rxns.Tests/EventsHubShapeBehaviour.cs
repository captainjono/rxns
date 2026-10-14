using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.Tests
{
    /// <summary>
    /// The shape a SignalR hub is allowed to have.
    ///
    /// <para>SignalR builds its method table by name alone and refuses a hub that offers the same
    /// name twice: "Duplicate definitions of 'X'. Overloading is not supported." It raises that
    /// while discovering hub methods, which happens as the host starts - so the failure is not a
    /// bad request, it is an application that never finishes starting. Every request afterwards,
    /// including a readiness probe and a load balancer's health check, gets a 500.</para>
    ///
    /// <para>A whole cluster went down that way: the arena bound its port, answered, and answered
    /// 500 to everything. The readiness probe wanted a 200 so the node never came up, and the load
    /// balancer marked it unhealthy and routed nothing, which read from outside as an arena that
    /// had never started at all.</para>
    ///
    /// <para>It compiles cleanly, so only running it - or this - catches it.</para>
    /// </summary>
    [TestClass]
    public class EventsHubShapeBehaviour
    {
        // What SignalR itself will enumerate: public instance methods declared on the hub, minus
        // the ones every Hub inherits.
        private static MethodInfo[] CallableMethods(Type hub) =>
            hub.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
               .Where(m => !m.IsSpecialName)
               .ToArray();

        [TestMethod]
        public void the_events_hub_offers_no_method_name_twice()
        {
            var duplicates = CallableMethods(typeof(EventsHub))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({g.Count()} overloads)")
                .ToArray();

            duplicates.Should().BeEmpty(
                "SignalR refuses to start a hub with an overloaded method, and the arena is that hub - " +
                "an overload here is a cluster that boots and then answers 500 to everything");
        }

        [TestMethod]
        public void the_events_hub_is_still_a_signalr_hub()
        {
            // The test above only means anything while this holds. If the hub stops being a Hub,
            // the constraint stops applying and the guard should be revisited rather than left
            // passing for the wrong reason.
            typeof(Hub).IsAssignableFrom(typeof(EventsHub))
                .Should().BeTrue("the overload rule is SignalR's, and it only binds a SignalR hub");
        }
    }
}
