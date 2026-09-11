using System;
using System.Threading;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    /// <summary>
    /// Host settings that only matter under load, and only on small machines.
    ///
    /// <para>Two defaults in the stack are wrong for a coordinator that fields a fleet's worth of
    /// small requests on two cores. The thread pool starts at about one thread per core and adds a
    /// couple a second, so a burst is served at the rate the pool grows rather than the rate the work
    /// arrives. And the socket accept backlog is short, so a host that falls behind has its pending
    /// connections dropped by the kernel - the client then waits out a TCP retransmit, which is
    /// seconds, and reports a failure the host never saw.</para>
    ///
    /// <para>A longer backlog is the more forgiving of the two failure modes: a queue drains when the
    /// spike passes and nothing is lost, whereas a dropped SYN is invisible to the server and costs
    /// the client seconds. Neither setting makes a saturated host fast - they decide how it behaves
    /// while it catches up.</para>
    ///
    /// <para>Both are overridable per environment, because the right values depend on the machine and
    /// guessing them once in code would be no better than the defaults being wrong.</para>
    /// </summary>
    public static class RxnsHostTuning
    {
        /// <summary>
        /// Worker threads to have ready before the pool starts throttling growth. Defaults to 16 per
        /// core, which covers a burst on a small host without the cost of parking hundreds of idle
        /// threads. Override with RXNS_MIN_THREADS.
        /// </summary>
        public static int MinWorkerThreads =>
            ReadInt("RXNS_MIN_THREADS", Math.Max(32, Environment.ProcessorCount * 16));

        /// <summary>
        /// Pending connections the kernel will hold before dropping them. Defaults to 1024 against
        /// Kestrel's 512. Linux also caps this at net.core.somaxconn, so a larger value here is a
        /// request rather than a guarantee. Override with RXNS_ACCEPT_BACKLOG.
        /// </summary>
        public static int AcceptBacklog => ReadInt("RXNS_ACCEPT_BACKLOG", 1024);

        /// <summary>
        /// Raises the pool floor. Only ever raises: another component may have set a higher floor for
        /// its own reasons, and lowering someone else's floor from generic host startup would be a
        /// surprising thing for this to do.
        /// </summary>
        public static void ApplyThreadFloor()
        {
            int workers, completion;
            ThreadPool.GetMinThreads(out workers, out completion);

            var wanted = MinWorkerThreads;
            if (wanted <= workers) return;

            ThreadPool.SetMinThreads(wanted, completion);
        }

        private static int ReadInt(string name, int fallback)
        {
            var configured = Environment.GetEnvironmentVariable(name);

            int parsed;
            return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured.Trim(), out parsed) && parsed > 0
                ? parsed
                : fallback;
        }
    }
}
