using System;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    /// <summary>How much of the web host's own diagnostic chatter is let through.</summary>
    public enum LogVerbosity
    {
        /// <summary>Every framework message from Information up - request start, routing, action, finish.</summary>
        Diagnostics = 0,

        /// <summary>Warnings and worse. The default: keeps what needs attention, drops the per-request narrative.</summary>
        Normal = 1,

        /// <summary>Errors only.</summary>
        Minimal = 2
    }

    /// <summary>
    /// Controls how much the ASP.NET host logs, at runtime.
    ///
    /// <para>This exists because framework diagnostics are not free here. Hosting, routing and MVC each
    /// log at Information, those go through <see cref="RxnsLogDebugProvider"/>, and in rxns a log
    /// message is itself published as an event - so a single request costs roughly four extra events
    /// before the handler does anything. At one heartbeat per worker per cadence that is the dominant
    /// load on the arena, and it is invisible because each line looks trivial on its own.</para>
    ///
    /// <para>Hardcoding a quieter level would trade that for an arena nobody can debug, so the level is
    /// a runtime switch instead: run quiet, and turn diagnostics back on for the minutes you need them
    /// without restarting and losing the state you were trying to look at.</para>
    ///
    /// <para>The host deliberately sets its own minimum to Trace and delegates the decision here.
    /// Filtering at the host would fix the level at startup; filtering here keeps it changeable, and
    /// costs only an IsEnabled call per message because ASP.NET checks that before formatting.</para>
    /// </summary>
    public static class RxnsLogVerbosity
    {
        private static LogVerbosity _level = ReadInitial();

        /// <summary>Current level. Set it to escalate or quieten a running host.</summary>
        public static LogVerbosity Level
        {
            get { return _level; }
            set { _level = value; }
        }

        /// <summary>Lowest framework level that reaches the log at the current setting.</summary>
        public static MsLogLevel FrameworkMinimum
        {
            get
            {
                switch (_level)
                {
                    case LogVerbosity.Diagnostics: return MsLogLevel.Information;
                    case LogVerbosity.Minimal: return MsLogLevel.Error;
                    default: return MsLogLevel.Warning;
                }
            }
        }

        /// <summary>
        /// Parses a level by name, for a config value or a remote command. Unrecognised input leaves
        /// the level alone rather than guessing - a typo in an ops command must not silently turn
        /// diagnostics on across a fleet.
        /// </summary>
        public static bool TrySet(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return false;

            LogVerbosity parsed;
            if (!Enum.TryParse(level.Trim(), true, out parsed)) return false;

            _level = parsed;
            return true;
        }

        /// <summary>
        /// Starting level from RXNS_LOG_VERBOSITY. Defaults to Normal: the per-request narrative is
        /// the expensive part and is rarely what anyone reads, while warnings and errors are both
        /// cheap and the reason to look at all.
        /// </summary>
        private static LogVerbosity ReadInitial()
        {
            var configured = Environment.GetEnvironmentVariable("RXNS_LOG_VERBOSITY");

            LogVerbosity parsed;
            if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse(configured.Trim(), true, out parsed))
                return parsed;

            return LogVerbosity.Normal;
        }
    }
}
