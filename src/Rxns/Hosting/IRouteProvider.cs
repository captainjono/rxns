using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Hosting
{
    public interface IRouteProvider
    {
        string GetLocalBaseRoute();
    }

    public class LocalRouteInfo : IRouteProvider, IDisposable
    {
        private readonly IRxnAppInfo _systemInfo;
        private ITenantCredentials _configuration;
        private readonly List<IDisposable> _resources = new List<IDisposable>();

        public LocalRouteInfo(IRxnAppInfo systemInfo, ITenantCredentials configuration)
        {
            _configuration = configuration;
            _systemInfo = systemInfo;
        }

        public string GetLocalBaseRoute()
        {
            return String.Format(@"{0}\{1}", _configuration.Tenant, _systemInfo.Name);
        }

        public void Dispose()
        {
            _resources.DisposeAll();
            _resources.Clear();
        }

    }
}
