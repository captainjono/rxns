using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.AppStatus.Host.Ai.Discovery
{
    /// <summary>
    /// Strategy for finding AI engine hosts WITHOUT a CIDR sweep — e.g. asking
    /// a vendor CLI ("foundry service status"), reading a running process's
    /// command line, parsing a service file, watching mDNS, etc. Each adapter
    /// is one such strategy; the controller calls them all and merges results.
    ///
    /// <para>Augmentation modules (a consumer app's support portal) can
    /// register their own adapter alongside the
    /// built-ins — same DI pattern as <see cref="IAiToolHandler"/> +
    /// <see cref="Rxns.AppStatus.Host.Monitor.IMonitorSource"/>.</para>
    /// </summary>
    public interface IAiEngineDiscoveryAdapter
    {
        /// <summary>Stable id used as a result-attribution tag on the wire
        /// (e.g. "foundry-cli", "ollama-process", "lmstudio-cli").</summary>
        string Id { get; }

        /// <summary>Human label shown in the UI alongside discovered results.</summary>
        string Label { get; }

        /// <summary>Run the discovery. Adapters MUST swallow their own
        /// "tool not installed" / "service not running" cases and just return
        /// an empty list — they're called speculatively on every Discover
        /// click; throwing would block other adapters.</summary>
        Task<IReadOnlyList<DiscoveredEngine>> DiscoverAsync(CancellationToken ct = default);
    }

    public class DiscoveredEngine
    {
        /// <summary>Endpoint base URL (e.g. <c>http://127.0.0.1:54997</c>).</summary>
        public string Url { get; set; }

        /// <summary>Engine kind that should be used when adopting this entry
        /// (<c>ollama</c> | <c>foundry</c> | <c>claude</c> | future kinds).</summary>
        public string Kind { get; set; }

        /// <summary>Models the host reported. Empty when the adapter couldn't
        /// (or didn't bother to) call <c>/v1/models</c>.</summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>How fast the verification probe came back, in ms. 0 when
        /// the adapter didn't perform a probe.</summary>
        public int LatencyMs { get; set; }

        /// <summary>The adapter id that produced this result. Stamped by the
        /// controller so the UI can group results by source.</summary>
        public string AdapterId { get; set; }
    }
}
