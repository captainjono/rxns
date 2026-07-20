using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Rxns.Microservices;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public class MicroServerBrokerProxy : ReportsStatusEventsHub<IMicroServerRegistryClient>, IMicroServerProxyRegistery
    {
        public IDictionary<string, MicroServerProxy> ActiveMicroServers = new Dictionary<string, MicroServerProxy>();
        private readonly ReplaySubject<dynamic> _slackBotProxies = new ReplaySubject<dynamic>(1);

        public class MicroServerProxy
        {
            public Type InterfaceToProxy { get; set; }
            public dynamic Proxy { get; set; }
            public IMicroServerRegistryClient Client { get; set; }
        }

        public void Register(string serivceTypeFullName)
        {
            var cfg = new MicroServerProxy()
            {
                Client = Clients.Caller,
                Proxy = Clients.Caller,
                InterfaceToProxy = FindType(serivceTypeFullName)
            };

            Register(cfg);
        }

        private void Register(MicroServerProxy cfg)
        {
            //if (cfg.InterfaceToProxy != typeof(ISlackBot))
            //{
            //    OnWarning("Cant register {0} at the moment", cfg.InterfaceToProxy.Name);
            //    return;
            //}

            ActiveMicroServers.Add(Context.ConnectionId, cfg);

            OnInformation("Registered '{0}' with connection '{1}'", cfg.InterfaceToProxy.Name, Context.ConnectionId);
            _slackBotProxies.OnNext(cfg.InterfaceToProxy);
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (ActiveMicroServers.ContainsKey(Context.ConnectionId))
            {
                OnWarning("Deactiving proxy for '{0}' because '{1}' disconnected", ActiveMicroServers[Context.ConnectionId].InterfaceToProxy.Name, Context.ConnectionId);
                ActiveMicroServers.Remove(Context.ConnectionId);
            }

            return base.OnDisconnectedAsync(exception);
        }

        //todo: fis security issues with this
        private static Type FindType(string fullName)
        {
            return
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.FullName.Equals(fullName));
        }
    }
}
