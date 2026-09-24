using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Rxns.Hosting;

namespace Rxns.Tests
{
    /// <summary>
    /// Reads what `top -l 1` says about a Mac.
    ///
    /// <para>Runs anywhere: the OS services are substituted and the two lines are the real output
    /// of a real Mac, captured. It needs no Mac to say whether the parsing is right, which is the
    /// point - the fleet that consumes these numbers is not a Mac either.</para>
    ///
    /// <para>Both figures are a percentage in use, because that is what every other platform's
    /// service reports and the reports that read them cannot tell which machine a number came
    /// from.</para>
    /// </summary>
    [TestClass]
    public class MacosBehavior
    {
        // Captured from `top -l 1`. 3.27 + 14.75 busy against 81.96 idle; 5807M in use with 10G
        // free, which this parser counts as 10000M.
        private const string CpuLine = "CPU usage: 3.27% user, 14.75% sys, 81.96% idle";
        private const string MemLine = "PhysMem: 5807M used (1458M wired), 10G unused.";

        private static MacOSSystemInformationService Sut() =>
            new MacOSSystemInformationService(Substitute.For<IOperationSystemServices>());

        [TestMethod]
        public void cpu_in_use_is_the_busy_time_as_a_percentage()
        {
            // user + sys. The earlier expectation of 0.1802 was a hundredth of what this returns
            // and of what Windows returns for the same busy-ness - nothing produced that scale.
            Sut().ParseCpu(CpuLine).Should().Be(18.02f, "user and sys together are the busy share");
        }

        [TestMethod]
        public void memory_in_use_is_a_percentage_not_a_free_fraction()
        {
            // 5807 of 15807. This used to answer 0.63 - the free share, as a fraction - so a Mac
            // running out of memory reported a smaller number the worse it got.
            Sut().ParseMem(MemLine).Should().BeApproximately(36.736888f, 0.0001f,
                "what is in use is what a capacity report is asking about");
        }

        [TestMethod]
        public void a_busier_mac_reports_a_higher_number()
        {
            var sut = Sut();

            var idle = sut.ParseMem("PhysMem: 1000M used (100M wired), 9G unused.");
            var busy = sut.ParseMem("PhysMem: 9000M used (100M wired), 1G unused.");

            busy.Should().BeGreaterThan(idle,
                "a reading that falls as pressure rises is worse than no reading at all");
        }

        [TestMethod]
        public void nothing_to_read_is_nothing_in_use()
        {
            var sut = Sut();

            sut.ParseCpu("").Should().Be(0, "an empty sample is not a busy machine");
            sut.ParseMem("").Should().Be(0, "an empty sample is not a full machine");
        }
    }
}
