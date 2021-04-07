using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using Rxns.Cloud;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;

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

        StringBuilder eventsAsJson = new StringBuilder();

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            eventsAsJson.Clear();
            return Rxn.Create<Unit>(o =>
            {
                events.ForEach(e => eventsAsJson.AppendFormat("{0}\r\n\r", e.GetPropertyDef("T") != null ? e.ToJson() : e.ToJson().ResolveAs(e.GetType())));

                return Connection.Call(c => c.PostAsync(WithBaseUrl("events/publish"), new StringContent(eventsAsJson.ToString(), Encoding.UTF8, "application/json"))).Select(_ => new Unit()).Subscribe(o);
            });
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

        public IObservable<string> PublishLog(Stream zippedLog)
        {
            var fileName = $"{DateTime.Now:dd-MM-yy-hhmmssfff}";
            OnVerbose("Publishing Log File : {0}", fileName);

            var uploadStream = new MultipartFormDataContent();
            uploadStream.Add(new StreamContent(zippedLog), fileName, fileName + ".zip");

            return Connection.Call(client => client.PostAsync(WithBaseUrl($"systemstatus/logs/{_credentials.Tenant}/{_systemInfo.Name}/publish"), uploadStream)).Select(_ => $"{_credentials.Tenant}/{_systemInfo.Name}_{fileName}");
        }

        public virtual IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            OnVerbose($"Publishing System Status to {BaseUrl()}");
            return _eventFactory.ToCommands(Connection.Call(client => client.PostAsJsonAsync(WithBaseUrl("systemstatus/heartbeat-2/publish"), new AppHeatbeat(status, meta))));
        }

        protected override string BaseUrl()
        {
            return _apps.AppStatusUrl;
        }
    }
}
