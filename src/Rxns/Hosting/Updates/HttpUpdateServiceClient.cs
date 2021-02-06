using System;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;

namespace Rxns.Hosting.Updates
{
    public class HttpUpdateServiceClient : AuthenticatedServiceClient, IUpdateStorageClient
    {
        public HttpUpdateServiceClient(IAppServiceRegistry cfg, IHttpConnection connection)
          : base(connection)
        {
            this.BaseUrl = cfg.AppStatusUrl;
        }

        public IObservable<bool> CreateUpdate(string systemName, string version, Stream appUpdate)
        {
            return Connection.Call(client => client.PostAsync(this.WithBaseUrl($"updates/{systemName}/{version}"), new MultipartFormDataContent()
            {
                {
                    new StreamContent(appUpdate),
                    version,
                    version + ".zip"
                }
            })).Select(_ => true);
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
    }
    
    
    public class AppUpdateInfo
    {
        public string Version { get; set; }
        public string SystemName { get; set; }
    }
}
