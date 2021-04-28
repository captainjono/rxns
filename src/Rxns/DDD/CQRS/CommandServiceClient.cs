using System;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using Rxns.DDD.Commanding;
using Rxns.CQRS;
using Rxns.Hosting;

namespace Rxns.DDD.CQRS
{
    public class CommandServiceClientCfg
    {
        public string BaseUrl { get; set; }
    }

    public class CommandServiceClient : AuthenticatedServiceClient, ICommandService
    {
        private readonly IAppServiceRegistry _services;
        private readonly IHttpConnection _anonConnection;
        private readonly ITenantCredentials _defaultCredentials;

        public CommandServiceClient(IAppServiceRegistry services, IHttpConnection connection, IHttpConnection anonConnection, ITenantCredentials defaultCredentials) : base(connection)
        {
            _services = services;
            _anonConnection = anonConnection;
            _defaultCredentials = defaultCredentials;
        }


        public IObservable<object> Run(IServiceCommand cmd)
        {
            return Connection.Call((httpClient, cancel) => httpClient.PostAsync(WithBaseUrl("cmd/{0}").FormatWith("na"), new StringContent(cmd.Serialise().ResolveAs(cmd.GetType()), Encoding.UTF8, "text/json"), cancel))
                          .SelectMany(r => r.Content.ReadAsStringAsync())
                          .Select(r => r.Deserialise<CommandResult>());
        }

        public IObservable<DomainQueryResult<T>> Run<T>(IDomainQuery<T> query)
        {
            var tenant = (query as IRequireTenantContext)?.Tenant;
            return Run<IDomainQuery<T>, DomainQueryResult<T>>(tenant ?? _defaultCredentials.Tenant, query);
        }

        public IObservable<DomainCommandResult<T>> Run<T>(IDomainCommand<T> cmd)
        {
            var tenant = (cmd as IRequireTenantContext)?.Tenant;

            return Run<IDomainCommand<T>, DomainCommandResult<T>>(tenant ?? _defaultCredentials.Tenant, cmd);
        }

        private IObservable<TR> Run<T, TR>(string tenant, T cmd)
        {
            return Connection.Call((c, cancel) => c.PostAsync(WithBaseUrl("cmd/{0}".FormatWith(tenant)), new StringContent(cmd.Serialise().ResolveAs(cmd.GetType()), Encoding.UTF8, "text/json"), cancel))
                .SelectMany(r => r.Content.ReadAsStringAsync())
                .Select(c => (TR)c.Deserialise(typeof(TR)));
        }


        public IObservable<ICommandResult> Run(string cmd)
        {
            return Connection.Call((c, cancel) => c.PostAsync(WithBaseUrl("cmd/{0}".FormatWith("embedded_in_command_cmd")), new StringContent(cmd, Encoding.UTF8, "text/json"), cancel))
                .SelectMany(r => r.Content.ReadAsStringAsync())
                .Select(c => CommandResult.Success());// c.Deserialise<object>().AsSuccessfulResult();
        }

        protected override string BaseUrl()
        {
            return _services.AppStatusUrl;
        }
    }
}
