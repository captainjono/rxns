using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Reliability;

namespace RxnCreate
{
    public static class RxnApps
    {
        public static IObservable<Unit> SpawnFromAppUpdate(string appName, string version, string binary, string appLocation)
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Spawning".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();

            var currentAppCfg = RxnAppCfg.LoadCfg(appLocation);

            var client = new AppUpdateServiceClient(new FileSystemAppUpdateRepo(new DotNetFileSystemService()), new DotNetFileSystemService(), currentAppCfg);
            client.ReportToDebug();

            return client.Download(appName, version, appLocation, new RxnAppCfg()
            {
                SystemName = appName,
                AppStatusUrl = currentAppCfg.AppStatusUrl,
                KeepUpdated = true,
                PathToSystemBinary =  Path.Combine(appLocation, binary),
                Version = version
            }).Select(_ =>
            {
                //spawn new app
                Process.Start(new ProcessStartInfo(Path.Combine(appLocation, binary)));

                return new Unit();
            });
        }

        public static IObservable<Unit> FromAppUpdate(string appName, string version, string binary, string appLocation, bool isLocal, string appStatusUrl = "http://localhost:888")
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Spawning".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();

            var currentAppCfg = RxnAppCfg.LoadCfg(appLocation);

            var client = AutoSelectUpdateServerClient(isLocal, appStatusUrl, currentAppCfg);
            return client.Download(appName, version, appLocation,new RxnAppCfg()
            {
                SystemName = appName,
                Version = version,
                PathToSystemBinary = Path.Combine(appLocation, binary),
                AppStatusUrl = appStatusUrl
            }, true).Select(_ =>
            {
                //spawn new app
                
                
                var resetPath = Assembly.GetEntryAssembly().Location;

                if(resetPath.EndsWith(".dll"))
                    resetPath = $"dotnet {resetPath}";
                
                Process.Start(resetPath, "");
                
                return new Unit();
            });
        }

        public static IObservable<Unit> CreateAppUpdate(string appName, string version, string appLocation, bool isLocal, string appStatusUrl = "http://localhost:888")
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Pushing update".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();

            var client = AutoSelectUpdateServerClient(isLocal, appStatusUrl, null);

            if (version.StartsWith("Latest-", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Split('-')[1];
                version = $"{version}.{DateTime.Now.ToString("s").Replace(":", "")}";
            }
            
            return client.Upload(appName, version, appLocation);
        }

        private static IUpdateServiceClient AutoSelectUpdateServerClient(bool isLocal, string appStatusUrl, IRxnAppCfg destCfg = null)
        {
            var fs = new DotNetFileSystemService();
            
            if (!isLocal)
            {
                appStatusUrl ??= "http://localhost:888";
                $"Using AppStatus URL: {appStatusUrl}".LogDebug();

                var c = new AppUpdateServiceClient(new HttpUpdateServiceClient(new AppServiceRegistry()
                {
                    AppStatusUrl = appStatusUrl
                }, new AnonymousHttpConnection(new HttpClient()
                {
                    Timeout = TimeSpan.FromHours(24)
                }, new ReliabilityManager(new RetryMaxTimesReliabilityCfg(3)))), fs, destCfg);
                c.ReportToDebug();
                return c;
            }
            else
            {
                "Saving update locally".LogDebug();

                var c = new AppUpdateServiceClient(new FileSystemAppUpdateRepo(fs), fs, destCfg);
                c.ReportToDebug();
                return c;
            }
        }
    }
}