using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rxns.Logging;

namespace Rxns.Hosting.Cluster
{
    public class AutoBalancingHostManager : IRxnHostManager
    {
        private IDictionary<Func<string, bool>, IRxnHost> _hosts = new Dictionary<Func<string, bool>, IRxnHost>();


        public AutoBalancingHostManager()
        {
        }

        public IRxnHost GetHostForApp(string name)
        {
            var hostForApp = _hosts.FirstOrDefault(h => h.Key(name)).Value;

            Manage(hostForApp);

            return hostForApp;
        }

        public IRxnHostManager ConfigureWith(IRxnHost host, Func<string, bool> hostsAllApps)
        {
            _hosts.Add(hostsAllApps, host);
            return this;
        }


        private void Manage(IRxnHost host)
        {
            $"Starting host {host.Name}".LogDebug();
            host.Start();
        }
    }

    public interface IRxnHostManager
    {
        IRxnHost GetHostForApp(string name);

        IRxnHostManager ConfigureWith(IRxnHost host, Func<string, bool> hostsAllApps);
    }
}
