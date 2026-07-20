using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    /// <summary>
    /// Process-wide counter of bytes flowing through the update / HTTP path,
    /// plus a pluggable progress hook so consumers can publish download
    /// lifecycle events without rxns taking a dependency on them.
    ///
    /// AppResourceInfo producers read TotalIn/TotalOut deltas at each emission
    /// to compute snapshot bytes/sec. Lock-free Interlocked counters; never
    /// reset during the process lifetime.
    ///
    /// The progress hook fires at Started / Extracting / Completed / Failed
    /// inside Download(); subscribers receive (worker, app, version, state,
    /// bytesSoFar, totalBytes, durationMs, message). Default no-op so this
    /// has zero effect when nothing is registered.
    /// </summary>
    public static class UpdateNetworkCounters
    {
        private static long _bytesIn;
        private static long _bytesOut;

        public static long TotalIn => Interlocked.Read(ref _bytesIn);
        public static long TotalOut => Interlocked.Read(ref _bytesOut);

        public static void AddIn(long n) { if (n > 0) Interlocked.Add(ref _bytesIn, n); }
        public static void AddOut(long n) { if (n > 0) Interlocked.Add(ref _bytesOut, n); }

        /// <summary>
        /// (worker, app, version, state, bytesSoFar, totalBytes, durationMs, message)
        /// State is one of: Started | Extracting | Completed | Failed | CacheHit.
        /// Set by the host process at startup; rxns has no dep on the consumer.
        /// </summary>
        public static Action<string, string, string, string, long, long, long, string> OnProgress;
    }

    public class AppUpdateServiceClient : ReportsStatus, IUpdateServiceClient
    {
        private readonly IUpdateStorageClient _updateService;
        private readonly IFileSystemService _fileSystem;
        private readonly IStoreAppUpdates _cmdService;
        private readonly IRxnAppCfg _cfg;

        public AppUpdateServiceClient(IUpdateStorageClient updateService, IFileSystemService fileSystem, IStoreAppUpdates cmdService, IRxnAppCfg forDownloadVersionCheck)
        {
            _fileSystem = fileSystem;
            _cmdService = cmdService;
            _cfg = forDownloadVersionCheck;
            _updateService = updateService;
        }

        // Per-process worker identifier (e.g. "{ClientId}/{AppName}#{instanceId}") set by the
        // host so progress events can be keyed per-worker. Falls back to MachineName if unset.
        public static string WorkerLabel = Environment.MachineName;

        private static void Progress(string app, string version, string state, long bytesSoFar, long totalBytes, long durationMs, string message = null)
        {
            try { UpdateNetworkCounters.OnProgress?.Invoke(WorkerLabel, app, version, state, bytesSoFar, totalBytes, durationMs, message); }
            catch { /* never let a publish hook break a download */ }
        }

        private string DeleteExistingAppIf(string system, string version, string destinationFolder, bool overwrite)
        {
            _cmdService.Run(new PrepareForAppUpdate(system, version, overwrite, destinationFolder));

            return version;
        }

        public IObservable<string> CheckForNewVersionAndCreateFolderForIt(string system, string version, string destinationFolder, bool overwrite)
        {
            //todo: update, need create dir {system}%%{version}
            return Rxn.Create<string>(o =>
            {
                if (version.IsNullOrWhiteSpace("Latest").BasicallyEquals("Latest"))
                {
                    return _updateService.ListUpdates(system).Select(a => a?.FirstOrDefault()).Select(latest =>
                    {
                        if (latest == null || latest.Version.IsNullOrWhitespace())
                        {
                            throw new Exception($"'{system}' not found on update server");
                        }

                        return DeleteExistingAppIf(system, latest.Version, destinationFolder, overwrite);
                    }).Subscribe(o);
                }

                return Rxn.Create(() => DeleteExistingAppIf(system, version, destinationFolder, overwrite)).Subscribe(o);
            });
        }

        public IObservable<string> KeepUpdated(string systemName, string version, string destinationFolderRoot, IRxnAppCfg cfg = null, bool overwrite = true)
        {
            return Download(systemName, version, destinationFolderRoot, cfg, overwrite)
                //untested - need to implement inside of tests also
                ;
        }

        public IObservable<string> Download(string system, string v, string destinationFolder = null, IRxnAppCfg cfg = null, bool overwrite = true)
        {
            return Rxn.DfrCreate(() => CheckForNewVersionAndCreateFolderForIt(system, v, destinationFolder, overwrite).SelectMany(version =>
            {
                if (version.IsNullOrWhitespace())//Already at the version we asked for
                {
                    OnVerbose("No new updates");
                    return string.Empty.ToObservable();
                }

                var targetPath = _cmdService.Run(new GetAppDirectoryForAppUpdate(system, v, destinationFolder)).WaitR();

                // Wait-on-lock + sentinel: when N worker processes on the same
                // host hit Download() concurrently for the same (system, version),
                // only ONE actually fetches + extracts; the others wait for the
                // exclusive lock, then see the .complete sentinel and skip.
                // Eliminates the "Autofac.dll already exists" race seen during
                // 2026-04-26 2x50 cluster runs (multiple workers per VM each
                // racing to extract the same zip into the same dir, half-
                // extracting state visible to in-process students → dotnet test
                // fails to load deps → no UnitTestOutcome emitted).
                //
                // - Sentinel <targetPath>/.complete is the trustable "fully
                //   extracted" marker — readers check this before using the dir.
                // - Lock <targetPath>.download.lock is process-scoped via
                //   FileShare.None on the OS handle. Crash-safe: OS releases
                //   on holder death so next waiter acquires immediately.
                // - Heartbeat <targetPath>.download.heartbeat is touched every
                //   ~10s by the holder while extracting. Waiters reclaim if no
                //   heartbeat has been seen for >30s — handles the stuck-but-
                //   alive case (zombie process still holding the OS lock).
                var sentinel = Path.Combine(targetPath, ".complete");
                if (File.Exists(sentinel))
                {
                    OnVerbose($"Cache hit: {system}@{version} already extracted at {targetPath}");
                    Progress(system, version, "CacheHit", 0, 0, 0);
                    return version.ToObservable();
                }

                var lockPath = $"{targetPath}.download.lock";
                var heartbeatPath = $"{targetPath}.download.heartbeat";
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? ".");

                while (true)
                {
                    try
                    {
                        using (var lockFs = new FileStream(lockPath, FileMode.OpenOrCreate,
                                                            FileAccess.ReadWrite, FileShare.None))
                        {
                            // Re-check sentinel inside the lock — a previous
                            // holder may have completed during our wait at the
                            // open() call.
                            if (File.Exists(sentinel))
                            {
                                OnVerbose($"Cache hit (post-lock): {system}@{version} at {targetPath}");
                                return version.ToObservable();
                            }

                            // Holder-side heartbeat: touch the heartbeat file
                            // every ~10s while we hold the lock, so waiters can
                            // tell us apart from a zombie holder.
                            File.WriteAllText(heartbeatPath, DateTime.UtcNow.ToString("O"));
                            using (var heartbeatCts = new CancellationTokenSource())
                            {
                                var heartbeatTask = Task.Run(async () =>
                                {
                                    while (!heartbeatCts.IsCancellationRequested)
                                    {
                                        try { await Task.Delay(TimeSpan.FromSeconds(10), heartbeatCts.Token); }
                                        catch (TaskCanceledException) { return; }
                                        try { File.WriteAllText(heartbeatPath, DateTime.UtcNow.ToString("O")); }
                                        catch { /* heartbeat best-effort */ }
                                    }
                                });

                                try
                                {
                                    DoDownloadAndExtract(system, version, targetPath, cfg);
                                    File.WriteAllText(sentinel, DateTime.UtcNow.ToString("O"));
                                }
                                finally
                                {
                                    heartbeatCts.Cancel();
                                    try { heartbeatTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
                                    try { File.Delete(heartbeatPath); } catch { }
                                }
                            }
                            return version.ToObservable();
                        }
                    }
                    catch (IOException)
                    {
                        // Lock held by another process — re-check sentinel (holder
                        // might've completed during our retry sleep).
                        if (File.Exists(sentinel))
                        {
                            OnVerbose($"Cache hit (during retry): {system}@{version} at {targetPath}");
                            return version.ToObservable();
                        }

                        // Heartbeat-based stale detection: if the holder's
                        // heartbeat is missing or hasn't been touched in >30s
                        // we treat the holder as dead/stuck and force-delete
                        // both files so the OS handle gets released (or the
                        // next acquire succeeds outright on a stuck-but-alive
                        // process whose heartbeat task crashed).
                        var stale = false;
                        try
                        {
                            if (!File.Exists(heartbeatPath))
                            {
                                stale = true;
                            }
                            else
                            {
                                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(heartbeatPath);
                                if (age > TimeSpan.FromSeconds(30))
                                {
                                    stale = true;
                                    OnVerbose($"Stale download lock detected for {system}@{version} (heartbeat age {age.TotalSeconds:0}s) — reclaiming");
                                }
                            }
                        }
                        catch { /* probe best-effort */ }

                        if (stale)
                        {
                            try { File.Delete(heartbeatPath); } catch { }
                            try { File.Delete(lockPath); } catch { /* OS may still hold; next acquire retries */ }
                        }

                        Thread.Sleep(500);
                    }
                }
            }));
        }

        // Synchronous fetch + extract path, wrapped under the wait-on-lock
        // gate in Download(). Extracted as a private method so the lock
        // window is unambiguous in the caller.
        //
        // Publishes WorkerAppUpdateProgress lifecycle events via the
        // UpdateNetworkCounters.OnProgress hook:
        //   Started     — lock acquired, about to fetch
        //   Extracting  — periodic during chunked stream copy (every ~1MB OR ~1s,
        //                 whichever is sparser, to avoid flooding SignalR)
        //   Completed   — final bytes, sentinel written
        //   Failed      — exception bubbling out
        // Bytes copied are also fed into UpdateNetworkCounters.AddIn so the
        // bfgHostResourceMonitor's AppResourceInfo emission can compute a
        // snapshot BytesInPerSec rate.
        private void DoDownloadAndExtract(string system, string version, string targetPath, IRxnAppCfg cfg)
        {
            var sw = Stopwatch.StartNew();
            Progress(system, version, "Started", 0, 0, 0);

            try
            {
                OnVerbose($"Downloading {system}@{version} to {targetPath}");
                // C# 7.3-compatible `using` block so this builds for both
                // netstandard2.0 and netstandard2.1.
                using (var ms = new MemoryStream())
                {
                    var content = _updateService.GetUpdate(system, version).Wait();

                    // Manual chunked copy so we can count bytes + emit Extracting
                    // progress without re-reading the stream. Chunk size 64KB is
                    // a sweet spot between syscall overhead and progress granularity.
                    var buffer = new byte[64 * 1024];
                    long total = 0;
                    long lastEmitBytes = 0;
                    var lastEmitAt = sw.ElapsedMilliseconds;
                    int read;
                    while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                        total += read;
                        UpdateNetworkCounters.AddIn(read);

                        // Throttle Extracting events to ~1MB OR ~1s sparser cadence —
                        // see WorkerAppUpdateProgress xmldoc for the rationale (don't
                        // flood the SignalR bus during a fast download).
                        var now = sw.ElapsedMilliseconds;
                        if (total - lastEmitBytes >= 1024 * 1024 || now - lastEmitAt >= 1000)
                        {
                            lastEmitBytes = total;
                            lastEmitAt = now;
                            Progress(system, version, "Extracting", total, 0, now);
                        }
                    }

                    if (ms.Length < 1)
                    {
                        // Empty stream from arena = upload was rejected/truncated server-side
                        // (catalogued 2026-04-27 — 30MB default cap silently truncated 41MB
                        // uploads to byte[0]). Throw so the caller knows + the sentinel below
                        // doesn't get written, otherwise we cache-poison: every subsequent
                        // Download() reads the .complete sentinel and skips the fetch even
                        // though no files exist on disk.
                        "Update not found".LogDebug();
                        Progress(system, version, "Failed", total, 0, sw.ElapsedMilliseconds, "Update not found");
                        throw new InvalidDataException($"Update '{system}@{version}' returned 0 bytes from arena — upload may have been rejected (size cap?) or the version is missing.");
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    OnVerbose("Extracting update to '{0}'", targetPath);

                    using (var contents = ZipFile.Read(ms))
                    {
                        contents.ExtractAll(targetPath, ExtractExistingFileAction.OverwriteSilently);
                    }

                    var targetCfg = cfg ?? RxnAppCfg.Detect(_cfg.Args);
                    targetCfg.Version = version;
                    targetCfg.Save().Save(targetPath);

                    if (!cfg?.AppPath.IsNullOrWhitespace() ?? false)
                    {
                        new CrossPlatformOperatingSystemServices().AllowToBeExecuted(cfg?.AppPath);
                    }

                    Progress(system, version, "Completed", total, total, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                Progress(system, version, "Failed", 0, 0, sw.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }

        public IObservable<Unit> Upload(string system, string version, string sourceFolder, string[] exclusions, string knownSha = null)
        {
            return Rxn.Create(() =>
            {
                OnVerbose("Uploading update for: {1} ({2} - '{0}')", sourceFolder, system, version);

                // knownSha is forwarded to the storage backend. The in-process
                // FileSystemAppUpdateRepo trusts it to skip writes for content
                // already on disk; HTTP-backed clients send it as a header so a
                // future server-side dedup can pick it up.
                var zippedUpdate = Zip(sourceFolder, "*.*", exclusions);
                return _updateService.CreateUpdate(system, version, zippedUpdate, knownSha).Select(_ => new Unit()).FinallyR(() =>
                {
                    OnVerbose($"Upload of {zippedUpdate.Length.ToFileSize()} complete");
                    zippedUpdate.Dispose();
                });
            });
        }


        public Stream Zip(string dir, string searchPattern, string[] exclusions)
        {
            if (dir == ".")
            {
                dir = Environment.CurrentDirectory;
            }

            var dirname = new DirectoryInfo(dir).Name;
            var memoryStream = new MemoryStream();
            //use folder as index in .zip if no slash
            var shouldNest = !(dir.EndsWith("/") || !dir.EndsWith("\\"));

            exclusions = exclusions.Concat(new [] {"%%"}).ToArray();

            using (var zipFile = new ZipFile())
            {
                foreach (string pathToFile in _fileSystem.GetFiles(dir.TrimEnd('/', '\\'), searchPattern, true)
                    .Select(fm => fm.Fullname))
                {
                    var absolute = _fileSystem.GetDirectoryPart(pathToFile);
                    
                    var relative = absolute.Replace(dir.TrimEnd('/', '\\'), "").LogDebug(pathToFile);
                    if (exclusions.Any(e => relative.BasicallyContains(e)))
                    {
                        //todo make detection more formal/optional
                        "detected nested update, skipping".LogDebug();
                        continue;
                    }

                    if (shouldNest)
                    {
                        relative = $"{dirname}{relative}";
                    }

                    zipFile.AddFile(pathToFile, relative);
                }

                zipFile.Save(memoryStream);
            }

            memoryStream.Seek(0L, SeekOrigin.Begin);
            return memoryStream;
        }
    }
}