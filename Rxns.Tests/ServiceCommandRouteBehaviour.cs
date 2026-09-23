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
            var sut = SutFor(@"NoTenant\W1_0", statusStore);
            var cmd = QuestionFor(@"NoTenant\W2_0");

            Execute(sut, cmd);

            statusStore.Received().Add(cmd);
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
