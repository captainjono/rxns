using System;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;

namespace Rxns.Hosting.Updates
{
    public class HttpUpdateServiceClient : AuthenticatedServiceClient, IUpdateStorageClient
    {
        private readonly IAppServiceRegistry _cfg;

        public HttpUpdateServiceClient(IAppServiceRegistry cfg, IHttpConnection connection)
          : base(connection)
        {
            _cfg = cfg;
        }

        public IObservable<bool> CreateUpdate(string systemName, string version, Stream appUpdate, string knownSha = null)
        {
            return Connection.Call(client =>
            {
                var multipart = new MultipartFormDataContent
                {
                    {
                        new StreamContent(appUpdate),
                        version,
                        version + ".zip"
                    }
                };
                // Forward the pre-computed sha so the server can short-circuit
                // its own hashing pass. Server is still authoritative — it
                // re-computes on receipt for the cross-process trust boundary.
                // Optional; server tolerates absence.
                if (!string.IsNullOrEmpty(knownSha))
                    multipart.Headers.Add("X-Content-Sha256", knownSha);
                return client.PostAsync(this.WithBaseUrl($"updates/{systemName}/{version}"), multipart);
            }).Select(_ => true);
        }

        public IObservable<Stream> GetUpdate(string systemName, string version)
        {
            return Connection.Call(client => client.GetAsync(this.WithBaseUrl($"updates/{systemName}/{(version ?? "Latest")}")))
                .SelectMany(resp =>
                {
                    resp.EnsureSuccessStatusCode();
                    return resp.Content.ReadAsStreamAsync();
                });
        }

        public IObservable<AppUpdateInfo[]> ListUpdates(string systemName, int top = 3)
        {
            return Connection.Call(client => client.GetAsync(WithBaseUrl(string.Format("updates/{0}/list?top={1}", systemName, top))))
                .SelectMany(resp =>
                {
                    resp.EnsureSuccessStatusCode();
                    return resp.Content.ReadAsStringAsync();
                }).Select(r => r.Deserialise<AppUpdateInfo[]>());
        }

        public IObservable<bool> RemoveUpdate(string systemName, string version)
        {
            return Connection.Call(client => client.DeleteAsync(this.WithBaseUrl($"updates/{systemName}/{version}")))
                .Select(resp => resp.IsSuccessStatusCode);
        }

        protected override string BaseUrl()
        {
            return _cfg.AppStatusUrl;
        }
    }
    
    
    public class AppUpdateInfo
    {
        public string Version { get; set; }
        public string SystemName { get; set; }

        /// <summary>
        /// SHA-256 of the .zip content for this update, hex-lowercase.
        /// Populated by content-addressed storage backends
        /// (<see cref="FileSystemAppUpdateRepo"/>). Null when unknown — older
        /// uploads without a sidecar or HTTP-backed updates that haven't been
        /// rehashed yet. Consumers (e.g. theBFG TestSuites extraction) should
        /// fall back to {SystemName, Version} when null.
        /// </summary>
        public string Sha256 { get; set; }

        /// <summary>
        /// Size in bytes of the update zip. Populated alongside Sha256.
        /// </summary>
        public long Size { get; set; }
    }
}
