using System;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.Hosting
{
    public class HttpAppStatusServiceClient : AuthenticatedServiceClient, IAppStatusServiceClient
    {
        private readonly ICreateEvents _eventFactory;
        private readonly ITenantCredentials _credentials;
        private readonly IAppServiceRegistry _apps;
        private readonly IRxnAppInfo _systemInfo;

        public HttpAppStatusServiceClient(IHttpConnection client, ICreateEvents eventFactory, IRxnAppInfo systemInfo, ITenantCredentials credentials, IAppServiceRegistry apps) : base(client)
        {
            _credentials = credentials;
            _apps = apps;
            _eventFactory = eventFactory;
            _systemInfo = systemInfo;

            BaseUrl = _apps.AppStatusUrl;
        }

        public IObservable<Unit> PublishError(SystemErrors error, SystemLogMeta[] meta)
        {
            return Connection.Call(client => client.PostAsJsonAsync(WithBaseUrl("errors/publish"), new { error, meta })).Select(_ => new Unit());
        }

        public IObservable<Unit> PublishError(ErrorReport report)
        {
            OnVerbose("Publishing error");
            return Connection.Call(client => client.PostAsync(WithBaseUrl("errors/report/publish"), report.ToJsonContent())).Select(_ => new Unit());
        }

        public IObservable<Unit> PublishError(BasicErrorReport report)
        {
            OnVerbose("Publishing error");
            return Connection.Call(client => client.PostAsync(WithBaseUrl("errors/basicReport/publish"), report.ToJsonContent())).Select(_ => new Unit());
        }

        public IObservable<Unit> DeleteError(long id)
        {
            OnVerbose("Deleting error");
            return Connection.Call(client => client.DeleteAsync(WithBaseUrl($"errors/{id}"))).Select(_ => new Unit());
        }

        public IObservable<Unit> PublishLog(Stream zippedLog)
        {
            var fileName = $"{DateTime.Now:dd-MM-yy-hhmmss}_logfile";
            OnVerbose("Publishing Log File : {0}", fileName);

            var uploadStream = new MultipartFormDataContent();
            uploadStream.Add(new StreamContent(zippedLog), fileName, fileName + ".zip");

            return Connection.Call(client => client.PostAsync(WithBaseUrl($"systemstatus/logs/{_credentials.Tenant}/{_systemInfo.Name}/publish"), uploadStream)).Select(_ => new Unit());
        }

        public IObservable<RxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, params object[] meta)
        {
            OnVerbose($"Publishing System Status to {BaseUrl}");
            return _eventFactory.ToCommands(Connection.Call(client => client.PostAsJsonAsync(WithBaseUrl("systemstatus/heartbeat-2/publish"), new { Status = status, Meta = meta })));
        }
    }
}
