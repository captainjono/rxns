using Rxns.DDD;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting.Updates;

namespace Rxns.Hosting
{
    /// <summary>
    /// Server-only registrations that used to live in <see cref="AppStatusCoreModule"/>
    /// but which a thin AppStatus *client* (e.g. an adapter that just publishes log
    /// entries to a central portal) doesn't need and shouldn't pay for.
    ///
    /// <para>Pulled in automatically by <see cref="AppStatusClientModule"/> for
    /// back-compat — every existing consumer of <c>Includes&lt;AppStatusClientModule&gt;()</c>
    /// gets the same set as before.</para>
    ///
    /// <para>New thin-client consumers (e.g. <c>YourApp.SupportPortalAdapter</c>)
    /// should compose <see cref="AppStatusCoreModule"/> + <c>HttpTransportModule</c>
    /// directly and skip this module. That avoids the eager-wiring NREs that occur
    /// when <see cref="LocalAppUpdateServer"/>'s ctor deps (<c>IAppStatusCfg</c>,
    /// <c>IRxnHostableApp</c>, etc.) aren't satisfied — those are server-side
    /// concerns and only a server-side host bothers to provide them.</para>
    /// </summary>
    public class AppStatusServerCoreModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                // Update server + store: server-side publish-update path. Needs
                // IAppStatusCfg / IRxnHostableApp / IUpdateServiceClient / etc.
                .CreatesOncePerApp<LocalAppUpdateServer>()
                .CreatesOncePerApp<CurrentDirectoryAppUpdateStore>(preserveExisting: true)
                // Command service: receives + dispatches server commands.
                .CreatesOncePerApp<AppCommandService>()
                .RespondsToSvcCmds<StreamLogs>()
                // Server status server: holds the server-side store.
                .CreatesOncePerApp<LocalAppStatusServer>()
                // Status publishers: take IAppUpdateManager (= LocalAppUpdateServer) so
                // they belong with the server side. Pulled out of Core to unblock thin
                // clients that don't run a server-side update path.
                .CreatesOncePerApp<AppSystemStatusPublisher>()
                .CreatesOncePerApp<SystemStatusPublisher>()
                .CreatesOncePerApp<SystemStatusService>();
        }
    }
}
