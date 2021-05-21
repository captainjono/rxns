using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Rxns.Hosting;

namespace Rxns.Tests
{
    [TestClass]
    public class MacosBehavior
    {
        [TestMethod]
        public void should_parse_macos_system_resources()
        {
            var cpuInfo = "CPU usage: 3.27% user, 14.75% sys, 81.96% idle";
            var memInfo = "PhysMem: 5807M used (1458M wired), 10G unused.";

            var osService = Substitute.For<IOperationSystemServices>();

            var sut = new MacOSSystemInformationService(osService);

            sut.ParseCpu(cpuInfo).Should().Be(0.18020001f, "the cpu should parse correctly");
            sut.ParseMem(memInfo).Should().Be(0.36736888F, "the mem should parse correctly");
        }
    }
}
