using System;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    /// <summary>
    /// Content-addressed file-system update storage.
    ///
    /// Layout under <c>updates/</c>:
    /// <code>
    /// updates/
    ///   content/&lt;sha&gt;.zip                  -- physical content; one per unique upload bytes
    ///   &lt;systemName&gt;-&lt;version&gt;.sha256       -- pointer file: contains the sha
    ///   &lt;systemName&gt;-&lt;version&gt;.zip          -- LEGACY ONLY; never written; back-compat read
    /// </code>
    ///
    /// Two uploads of byte-identical content under different versions share one
    /// <c>content/&lt;sha&gt;.zip</c>; the per-version sidecars are tiny pointers.
    /// Re-running the same test suite many times produces N sidecars and 1 zip
    /// regardless of how many distinct versions get assigned.
    ///
    /// <para><b>Dedup-on-write:</b> <see cref="CreateUpdate"/> hashes the stream
    /// as it writes to a temp file. On completion, if <c>content/&lt;sha&gt;.zip</c>
    /// already exists, the temp is deleted (skip). Otherwise renamed into place.
    /// Either way, a sidecar is written/updated. <c>knownSha</c> from the caller
    /// short-circuits the hash pass for in-process callers (e.g. theBFG) that
    /// already computed the source-content sha.</para>
    ///
    /// <para><b>GC:</b> <see cref="TruncateUpdatesIf"/> still rolls per-system at
    /// <see cref="AppUpdateCfg.NumberOfRollingAppUpdates"/>. After deleting a
    /// sidecar, if no other sidecar references the same sha, the orphaned
    /// <c>content/&lt;sha&gt;.zip</c> is also deleted.</para>
    ///
    /// <para><b>Back-compat:</b> Pre-existing <c>&lt;sys&gt;-&lt;ver&gt;.zip</c> files
    /// (uploaded before this layout shipped) still read via the legacy path in
    /// <see cref="GetUpdate"/>. <see cref="ListUpdates"/> surfaces them with
    /// <c>Sha256 = null</c>. On the next <see cref="CreateUpdate"/> for the same
    /// (system, version) they get migrated to the new layout naturally.</para>
    /// </summary>
    public class FileSystemAppUpdateRepo : IUpdateStorageClient
    {
        // Sidecar filenames are <systemName>-<version>.sha256. We need to recognise
        // them when enumerating; content blobs live in a subdirectory, so the only
        // ambiguity is between sidecars and legacy <sys>-<ver>.zip files which both
        // sit at the updates/ root.
        private const string ContentDirName = "content";
        private const string SidecarExt     = ".sha256";

        private readonly AppUpdateCfg _cfg;
        private readonly IFileSystemService _fs;
        private readonly IAppStatusCfg _appCfg;
        public string UpdateDir { get; set; }

        public FileSystemAppUpdateRepo(IFileSystemService fs, IAppStatusCfg appCfg)
        {
            _cfg = new AppUpdateCfg() { NumberOfRollingAppUpdates = 3 };
            _fs = fs;
            _appCfg = appCfg;

            UpdateDir = Path.Combine(_appCfg.AppRoot, "updates");
        }

        public IObservable<bool> CreateUpdate(string systemName, string version, Stream update, string knownSha = null)
        {
            return Rxn.Create(() =>
                {
                    EnsureDir(UpdateDir);
                    EnsureDir(ContentDir());

                    update.Seek(0, SeekOrigin.Begin);

                    string sha;
                    long bytesStaged;
                    string tempPath = Path.Combine(ContentDir(), $".upload-{Guid.NewGuid():N}.tmp");

                    try
                    {
                        if (!string.IsNullOrEmpty(knownSha))
                        {
                            // Trust path: caller has pre-hashed the source content
                            // (in-process). If content/<knownSha>.zip already exists,
                            // skip the stream copy entirely -- byte-identical content
                            // is already on disk. Otherwise write the stream as-is.
                            sha = knownSha.ToLowerInvariant();
                            var existing = ContentPath(sha);
                            if (File.Exists(existing))
                            {
                                bytesStaged = new FileInfo(existing).Length;
                                $"[updates] dedup hit (trusted): {systemName}@{version} sha {Short(sha)} -- content already present, skipped write"
                                    .LogDebug();
                            }
                            else
                            {
                                using (var dest = _fs.CreateWriteableFile(tempPath))
                                {
                                    update.CopyTo(dest);
                                    bytesStaged = dest.Length;
                                }
                                MoveOrSwallow(tempPath, existing);
                            }
                        }
                        else
                        {
                            // Compute path: stream-hash while writing to temp. On
                            // completion, check if content/<sha>.zip already exists;
                            // if yes, drop the temp; if no, rename temp into place.
                            using (var dest = _fs.CreateWriteableFile(tempPath))
                            using (var hasher = SHA256.Create())
                            using (var crypto = new CryptoStream(dest, hasher, CryptoStreamMode.Write))
                            {
                                update.CopyTo(crypto);
                                crypto.FlushFinalBlock();
                                sha = ToHexLower(hasher.Hash);
                                bytesStaged = dest.Length;
                            }
                            var target = ContentPath(sha);
                            if (File.Exists(target))
                            {
                                $"[updates] dedup hit (server-hashed): {systemName}@{version} sha {Short(sha)} -- discarding duplicate {bytesStaged} bytes"
                                    .LogDebug();
                                TryDelete(tempPath);
                            }
                            else
                            {
                                MoveOrSwallow(tempPath, target);
                                $"[updates] new content: {systemName}@{version} sha {Short(sha)} ({bytesStaged} bytes)".LogDebug();
                            }
                        }

                        // Sidecar = version-entity pointer. Idempotent — overwrites
                        // any prior sha for this (system, version) pair (content drift).
                        File.WriteAllText(SidecarPath(systemName, version), sha);

                        // If the version previously pointed to a different sha and
                        // that sha is now orphaned, GC it. Cheap O(sidecars-count).
                        GcOrphanedContent();
                    }
                    finally
                    {
                        TryDelete(tempPath);
                    }

                    return true;
                })
                .Do(_ => TruncateUpdatesIf(systemName));
        }

        public IObservable<Stream> GetUpdate(string systemName, string version = null)
        {
            EnsureDir(UpdateDir);

            return Rxn.Create(() =>
                {
                    // New layout: sidecar -> content/<sha>.zip
                    var sidecar = SidecarPath(systemName, version);
                    if (File.Exists(sidecar))
                    {
                        var sha = ReadSidecar(sidecar);
                        if (sha != null)
                        {
                            var content = ContentPath(sha);
                            if (File.Exists(content))
                                return _fs.GetReadableFile(content);
                        }
                    }

                    // Legacy: <sys>-<ver>.zip at updates/ root
                    return _fs.GetReadableFile(_fs.PathCombine(UpdateDir, GetLegacyZipName(systemName, version)));
                })
                .Catch<Stream, Exception>(e =>
                {
                    ReportStatus.Log.OnWarning($"While downloading update {e}");
                    throw new DomainCommandException(String.Format("Could not find '{0}' version '{1}'", systemName, version));
                });
        }

        public IObservable<AppUpdateInfo[]> ListUpdates(string systemName, int top = 3)
        {
            if (!_fs.ExistsDirectory(UpdateDir))
                return Rxn.Empty<AppUpdateInfo[]>();

            var wantAll = systemName.IsNullOrWhiteSpace("all").Equals("all", StringComparison.OrdinalIgnoreCase);

            // New-layout entries: enumerate *.sha256 sidecars (filtered by system).
            var sidecarPattern = wantAll ? $"*{SidecarExt}" : $"{systemName}-*{SidecarExt}";
            var sidecars = _fs.GetFiles(UpdateDir, sidecarPattern)
                .Select(f =>
                {
                    var baseName = f.Name.Substring(0, f.Name.Length - SidecarExt.Length);
                    var dashIdx = baseName.IndexOf('-');
                    if (dashIdx <= 0) return null;
                    var sys = baseName.Substring(0, dashIdx);
                    var ver = baseName.Substring(dashIdx + 1);
                    var sha = ReadSidecar(f.Fullname);
                    long size = 0;
                    if (sha != null)
                    {
                        try { size = new FileInfo(ContentPath(sha)).Length; } catch { }
                    }
                    return new { File = f, Info = new AppUpdateInfo { SystemName = sys, Version = ver, Sha256 = sha, Size = size } };
                })
                .Where(x => x != null);

            // Legacy entries: <sys>-<ver>.zip at root WITHOUT a matching sidecar.
            // (When a matching sidecar exists, the .zip at root is also legacy but
            // ignored — sidecar wins. In practice once migrated, no legacy zips
            // exist at root.)
            var legacyPattern = wantAll ? "*.zip" : $"{systemName}-*.zip";
            var legacies = _fs.GetFiles(UpdateDir, legacyPattern)
                .Where(f => !File.Exists(_fs.PathCombine(UpdateDir, f.Name.Substring(0, f.Name.Length - 4) + SidecarExt)))
                .Select(f =>
                {
                    var baseName = f.Name.Substring(0, f.Name.Length - 4); // strip .zip
                    var dashIdx = baseName.IndexOf('-');
                    if (dashIdx <= 0) return null;
                    return new { File = f, Info = new AppUpdateInfo {
                        SystemName = baseName.Substring(0, dashIdx),
                        Version    = baseName.Substring(dashIdx + 1),
                        Sha256     = null,
                        Size       = f.Length
                    } };
                })
                .Where(x => x != null);

            return sidecars.Concat(legacies)
                .OrderByDescending(x => x.File.LastWriteTime)
                .Take(top)
                .Select(x => x.Info)
                .ToArray()
                .ToObservable()
                .Catch<AppUpdateInfo[], Exception>(_ => Rxn.Empty<AppUpdateInfo[]>());
        }

        public IObservable<bool> RemoveUpdate(string systemName, string version)
        {
            return Rxn.Create(() =>
            {
                if (string.IsNullOrWhiteSpace(systemName) || string.IsNullOrWhiteSpace(version))
                    return false;

                var sidecar = SidecarPath(systemName, version);
                var legacyZip = _fs.PathCombine(UpdateDir, GetLegacyZipName(systemName, version));
                var anyFound = false;

                if (File.Exists(sidecar))
                {
                    TryDelete(sidecar);
                    anyFound = true;
                }
                if (File.Exists(legacyZip))
                {
                    TryDelete(legacyZip);
                    anyFound = true;
                }
                if (anyFound) GcOrphanedContent();
                return anyFound;
            });
        }

        private void TruncateUpdatesIf(string systemName)
        {
            ListUpdates(systemName, 100).Do(all =>
            {
                var total = all.Length;
                while (total > _cfg.NumberOfRollingAppUpdates)
                {
                    var victim = all[--total];
                    victim.Version.LogDebug("ROLLING DELETE");
                    DeleteUpdate(victim.SystemName, victim.Version);
                }
            }).Until();
        }

        private void DeleteUpdate(string systemName, string version)
        {
            TryDelete(SidecarPath(systemName, version));
            TryDelete(_fs.PathCombine(UpdateDir, GetLegacyZipName(systemName, version)));
            GcOrphanedContent();
        }

        /// <summary>
        /// Delete <c>content/&lt;sha&gt;.zip</c> files no longer referenced by any
        /// sidecar. Run after any sidecar mutation. O(blobs * sidecars) in the
        /// worst case which is fine at expected N=3..30 sidecars total.
        /// </summary>
        private void GcOrphanedContent()
        {
            var contentDir = ContentDir();
            if (!Directory.Exists(contentDir)) return;

            var allSidecars = _fs.GetFiles(UpdateDir, $"*{SidecarExt}").ToList();
            var referenced = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in allSidecars)
            {
                var sha = ReadSidecar(s.Fullname);
                if (sha != null) referenced.Add(sha);
            }

            foreach (var blob in Directory.GetFiles(contentDir, "*.zip"))
            {
                var sha = Path.GetFileNameWithoutExtension(blob);
                if (!referenced.Contains(sha))
                {
                    TryDelete(blob);
                    $"[updates] GC: orphaned content {Short(sha)}.zip".LogDebug();
                }
            }
        }

        // --- helpers -------------------------------------------------------

        private string ContentDir() => Path.Combine(UpdateDir, ContentDirName);
        private string ContentPath(string sha) => Path.Combine(ContentDir(), $"{sha}.zip");
        private string SidecarPath(string systemName, string version) => Path.Combine(UpdateDir, $"{systemName}-{version}{SidecarExt}");
        private static string GetLegacyZipName(string systemName, string version) => $"{systemName}-{version}.zip";

        private void EnsureDir(string path)
        {
            if (!_fs.ExistsDirectory(path)) _fs.CreateDirectory(path);
        }

        private static string ReadSidecar(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(text) ? null : text.ToLowerInvariant();
            }
            catch { return null; }
        }

        private static void MoveOrSwallow(string src, string dest)
        {
            try
            {
                // Race: another process completed the same upload while we were
                // staging. In that case dest already exists with byte-identical
                // content (same sha). Drop our temp and call it a win.
                if (File.Exists(dest))
                {
                    TryDelete(src);
                    return;
                }
                File.Move(src, dest);
            }
            catch (IOException)
            {
                // Lost the race — dest now exists. Drop our temp.
                TryDelete(src);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        private static string ToHexLower(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            for (int i = 0, j = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                c[j++] = (char)((b >> 4) > 9 ? (b >> 4) + 0x57 : (b >> 4) + 0x30);
                c[j++] = (char)((b & 0xF) > 9 ? (b & 0xF) + 0x57 : (b & 0xF) + 0x30);
            }
            return new string(c);
        }

        private static string Short(string sha)
        {
            return sha == null ? "(null)" : (sha.Length > 12 ? sha.Substring(0, 12) : sha);
        }
    }
}
