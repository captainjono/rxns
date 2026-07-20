using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Subjects;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting.Auth;
using Rxns.Hosting.Compression;
using Rxns.Hosting.Updates;
using Rxns.Logging;
using Rxns.WebApi.Compression;

namespace Rxns.Hosting
{

    public class InsecureApiNoAuthRequired : ReportStatus, IAuthenticationService<AccessToken, ITenantCredentials>
    {
        public ITenantCredentials Credentials { get; set; }
        public bool RequiresSSL { get; set; }
        public IObservable<AccessToken> Tokens { get; } = _noToken;
        public IObservable<bool> IsAuthenticated { get; } = _alwaysAuthed;

        private static ISubject<bool> _alwaysAuthed = new BehaviorSubject<bool>(true);
        private static ISubject<AccessToken> _noToken = new BehaviorSubject<AccessToken>(new AccessToken());
        public IObservable<AccessToken> Refresh()
        {
            return new AccessToken().ToObservable();
        }

        public IObservable<AccessToken> Login(ITenantCredentials credentials)
        {
            return Refresh();
        }

        public IObservable<AccessToken> GetToken(ITenantCredentials credentials)
        {
            return Refresh();
        }
    }

    /// <summary>
    /// Backwards-compat composite: the old monolithic module that mixed
    /// transport-agnostic services with HTTP-transport bindings AND server-side
    /// registrations. Now a thin wrapper that pulls in
    /// <see cref="AppStatusCoreModule"/> + <see cref="AppStatusServerCoreModule"/>
    /// + <see cref="HttpTransportModule"/> so the dozens of existing
    /// <c>.Includes&lt;AppStatusClientModule&gt;()</c> call-sites keep working
    /// unchanged.
    ///
    /// <para>
    /// New code that wants a non-HTTP transport (e.g. Redis Streams) should
    /// include <see cref="AppStatusCoreModule"/> directly + the matching
    /// transport module instead of this composite, so there's no last-
    /// registration-wins fight on <c>IAppStatusServiceClient</c> /
    /// <c>IEventHub</c>.
    /// </para>
    ///
    /// <para>
    /// New code that is a *thin AppStatus client* (publishes log entries but
    /// doesn't host the portal itself — adapters, in-process bridges) should
    /// compose <see cref="AppStatusCoreModule"/> + <see cref="HttpTransportModule"/>
    /// and SKIP <see cref="AppStatusServerCoreModule"/>. Otherwise eager-wiring
    /// of <see cref="LocalAppUpdateServer"/> trips on missing
    /// <c>IAppStatusCfg</c> / <c>IRxnHostableApp</c> — server-only concerns.
    /// </para>
    /// </summary>
    public class AppStatusClientModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            new AppStatusCoreModule().Load(lifecycle);
            new AppStatusServerCoreModule().Load(lifecycle);
            new HttpTransportModule().Load(lifecycle);
            return lifecycle;
        }
    }
}
