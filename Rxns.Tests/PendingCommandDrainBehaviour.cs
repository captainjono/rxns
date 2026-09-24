using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Rxns.DDD.Commanding;
using Rxns.Hosting.Updates;

namespace Rxns.Tests
{
    /// <summary>
    /// A command queued for a route nobody owns yet has to come back out when that route arrives.
    ///
    /// <para>Companion to <see cref="ServiceCommandRouteBehaviour"/>, which established that route
    /// matching must be case-insensitive at every comparison site. The pending queue was a site that
    /// was missed: <c>Add</c> keyed it by the destination exactly as written while the channel
    /// lookup, the route registry and the polling set all normalise through <c>AsRoute()</c>. A
    /// worker that says <c>NoTenant\W1_0</c> in one message and <c>notenant\w1_0</c> in the next is
    /// two different routes to that dictionary.</para>
    ///
    /// <para>Observed live on a theBFG cluster running without redis: one dispatch of two queued and
    /// never drained. The worker stayed connected and heartbeating the whole time, so every status
    /// view showed a healthy fleet while the run waited for a result that could no longer be
    /// delivered.</para>
    /// </summary>
    [TestClass]
    public class PendingCommandDrainBehaviour
    {
        private static IRxnQuestion CommandFor(string destination)
        {
            var cmd = Substitute.For<IRxnQuestion>();
            cmd.Destination.Returns(destination);
            cmd.Options.Returns("StartUnitTest");
            return cmd;
        }

        // No channels registered, so every Add falls through to the pending queue - which is the
        // state a real arena is in before its workers have registered.
        private static RoutableAppCmdManager Unrouted() => new RoutableAppCmdManager();

        [TestMethod]
        public void a_queued_command_drains_for_the_route_it_was_addressed_to()
        {
            var sut = Unrouted();
            sut.Add(CommandFor("NoTenant\\W1_0"));

            sut.FlushCommands("NoTenant\\W1_0")
                .Should().HaveCount(1, "a command queued for a route is what that route collects");
        }

        [TestMethod]
        public void a_queued_command_drains_however_the_route_was_capitalised()
        {
            // The two spellings a live cluster actually used, one per message.
            var sut = Unrouted();
            sut.Add(CommandFor("NoTenant\\W1_0"));

            sut.FlushCommands("notenant\\w1_0")
                .Should().HaveCount(1, "the same worker spelled two ways is still one worker");
        }

        [TestMethod]
        public void a_command_queued_in_lower_case_drains_for_the_mixed_case_route()
        {
            var sut = Unrouted();
            sut.Add(CommandFor("notenant\\w1_0"));

            sut.FlushCommands("NoTenant\\W1_0")
                .Should().HaveCount(1, "neither spelling is the canonical one");
        }

        [TestMethod]
        public void draining_one_route_leaves_another_alone()
        {
            var sut = Unrouted();
            sut.Add(CommandFor("NoTenant\\W1_0"));
            sut.Add(CommandFor("NoTenant\\W2_0"));

            sut.FlushCommands("notenant\\w1_0").Should().HaveCount(1);
            sut.FlushCommands("notenant\\w2_0")
                .Should().HaveCount(1, "one worker collecting its work does not consume another's");
        }

        [TestMethod]
        public void a_drained_command_is_not_delivered_twice()
        {
            var sut = Unrouted();
            sut.Add(CommandFor("NoTenant\\W1_0"));

            sut.FlushCommands("NoTenant\\W1_0").Should().HaveCount(1);
            sut.FlushCommands("NoTenant\\W1_0")
                .Should().BeEmpty("a command that has been handed over is gone");
        }

        [TestMethod]
        public void a_command_queued_during_a_drain_is_not_lost()
        {
            // The queue used to be taken out of the dictionary and then emptied. Anything that
            // enqueued in between landed in an object nothing could reach afterwards: not
            // delivered, not queued, not reported. Reconnects make that window real - the worker
            // registers as the arena is dispatching to it.
            var sut = Unrouted();

            for (var attempt = 0; attempt < 200; attempt++)
            {
                var route = "NoTenant\\W" + attempt;

                var queueing = Task.Run(() => sut.Add(CommandFor(route)));
                var draining = Task.Run(() => sut.FlushCommands(route).Count());

                Task.WaitAll(queueing, draining);

                var drainedNow = draining.Result;
                var stillQueued = sut.FlushCommands(route).Count();

                (drainedNow + stillQueued).Should().Be(1,
                    "a command is either handed over or still waiting - never neither");
            }
        }
    }
}
