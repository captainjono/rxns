using System;

namespace Rxns.Cloud
{
    public static class RouteExtensions
    {
        public static string GetRoute(this IRouteAware context)
        {
            return GetRoute(context.Tenant, context.SystemName);
        }

        public static string GetRoute(string tenant, string systemName, string service = "")
        {
            return String.Format("{0}\\{1}\\{2}", tenant, systemName, service).TrimEnd('\\');
        }
    }
}
