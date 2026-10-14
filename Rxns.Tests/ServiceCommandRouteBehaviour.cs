using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;

namespace Rxns.Tests
{
    /// <summary>
    /// Route matching must be case-insensitive on every comparison site.
    ///
    /// <para>Senders normalise a destination through <c>AsRoute()</c>, which lowercases
    /// (<c>IServiceCommand.AsRoute</c>), and <c>IsFor</c> normalises both sides before comparing.
    /// <c>ServiceCommandExecutor.Process</c> did not - it compared the already-lowercased
    /// Destination against a raw <c>GetLocalBaseRoute()</c> of <c>{Tenant}\{SystemName}</c>. A worker
    /// identifying as <c>NoTenant\W1_0</c> therefore rejected every command addressed to
    /// <c>notenant\w1_0</c> and queued it forever, logging
    /// "Queued command to notenant\w1_0 because its not local NoTenant\W1_0".</para>
    ///
    /// <para>Observed live on a theBFG cluster: the hub dispatched StartUnitTest, no worker ever
    /// accepted it, no test ran, and the client blocked after uploading its payload.</para>
    /// </summary>
    [TestClass]
    public class ServiceCommandRouteBehaviour
    {
        private static ServiceCommandExecutor SutFor(string localRoute, IAppCmdManager statusStore)
        {
            var cfg = Substitute.For<IRxnAppCfg>();
            cfg.Args.Returns(new string[0]);

            var local = Substitute.For<IRouteProvider>();
            local.GetLocalBaseRoute().Returns(localRoute);

            return new ServiceCommandExecutor(
                Substitute.For<IServiceCommandFactory>(),
                Substitute.For<IResolveTypes>(),
                Substitute.For<ICommandService>(),
                cfg,
                local,
                statusStore);
        }

        private static IRxnQuestion QuestionFor(string destination)
        {
            var cmd = Substitute.For<IRxnQuestion>();
            cmd.Destination.Returns(destination);
            cmd.Options.Returns("shell");
            return cmd;
        }

        /// <summary>
        /// The repro. A destination differing from the local route only in case is the SAME route,
        /// so it must not be queued as remote.
        /// </summary>
        [TestMethod]
        public void should_treat_a_route_differing_only_in_case_as_local()
        {
            var statusStore = Substitute.For<IAppCmdManager>();
            var sut = SutFor(@"NoTenant\W1_0", statusStore);

            Execute(sut, QuestionFor(@"notenant\w1_0"));

            statusStore.DidNotReceive().Add(Arg.Any<IRxnQuestion>());
        }

        /// <summary>Control: an exact-case match has always been treated as local.</summary>
        [TestMethod]
        public void should_treat_an_exactly_matching_route_as_local()
        {
            var statusStore = Substitute.For<IAppCmdManager>();
            var sut = SutFor(@"NoTenant\W1_0", statusStore);

            Execute(sut, QuestionFor(@"NoTenant\W1_0"));

            statusStore.DidNotReceive().Add(Arg.Any<IRxnQuestion>());
        }

        /// <summary>Control: a genuinely different route must still be queued for its owner.</summary>
        [TestMethod]
        public void should_queue_a_command_bound_for_another_route()
        {
            var statusStore = Substitute.For<IAppCmdManager>();

            // Stubbed true because this node can reach the addressee. Without it the substitute
            // answers false, the guard below returns early, and the test reads as a routing
            // failure when nothing was ever going to be queued.
            statusStore.CanRoute(Arg.Any<IRxnQuestion>()).Returns(true);

            var sut = SutFor(@"NoTenant\W1_0", statusStore);
            var cmd = QuestionFor(@"NoTenant\W2_0");

            Execute(sut, cmd);

            statusStore.Received().Add(cmd);
        }

        /// <summary>
        /// A node only takes custody of someone else's command when it can actually deliver it.
        ///
        /// <para>Commands reach every node on the shared transport and each decides whose it is, so
        /// the addressee already holds its own copy - there is nothing to forward. A worker that
        /// queued it anyway parked it against a route it has no channel to, where nothing drains
        /// it: on a two-VM cluster that swallowed half a dispatch while the orchestrator reported a
        /// clean firing.</para>
        /// </summary>
        [TestMethod]
        public void should_not_queue_a_command_it_has_no_way_to_deliver()
        {
            var statusStore = Substitute.For<IAppCmdManager>();
            statusStore.CanRoute(Arg.Any<IRxnQuestion>()).Returns(false);

            var sut = SutFor(@"NoTenant\W1_0", statusStore);
            var cmd = QuestionFor(@"NoTenant\W2_0");

            Execute(sut, cmd);

            statusStore.DidNotReceive().Add(Arg.Any<IRxnQuestion>());
        }

        /// <summary>
        /// Runs Process and swallows only the parse-stage NullReference that follows a LOCAL
        /// decision - substituted command factories parse nothing, so reaching that point is itself
        /// evidence the command was accepted as local. The routing decision is what is asserted.
        /// </summary>
        private static void Execute(ServiceCommandExecutor sut, IRxnQuestion cmd)
        {
            try { sut.Process(cmd); }
            catch (NullReferenceException) { }
        }
    }
}
