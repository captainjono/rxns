using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.AppStatus.Host
{
    /// <summary>
    /// <c>IContainerPostBuildService</c> registered into the host's container so a
    /// reference to the resolver is captured after the container builds. Tests +
    /// in-process diagnostics resolve <see cref="AppStatusHostDiagnostics.Resolver"/>
    /// to inspect <c>IAppContainer</c> / <c>InMemoryAppStatusStore</c> without
    /// reaching through HTTP.
    ///
    /// Intentionally minimal: holds a static; no rxns hooks beyond capture. Safe to
    /// leave in production — the static is a single reference, never disposed.
    /// </summary>
    public sealed class AppStatusHostDiagnostics : IContainerPostBuildService
    {
        /// <summary>The host's resolver, captured once the container is built. Null until then.</summary>
        public static IResolveTypes Resolver { get; private set; }

        public System.IObservable<System.Reactive.Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            Resolver = container;
            return Observable.Return(new System.Reactive.Unit());
        }
    }
}
