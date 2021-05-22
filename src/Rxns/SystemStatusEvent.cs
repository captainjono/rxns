using System;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns
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
    
    public interface IRouteAware
    {
        string Tenant { get; set; }
        string SystemName { get; set; }
    }

    public class SystemRxn : IUniqueRxn
    {
        /// <summary>
        /// The type of the class, used for serialisation
        /// </summary>
        public string T { get { return GetType().FullName; } }
        /// <summary>
        /// the id of the event
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// the timestamp of the event
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The tenant the event occoured on
        /// </summary>
        public string Tenant { get; set; }

        /// <summary>
        /// The message to send to the user
        /// </summary>
        public string Message { get; set; }

        public SystemRxn()
        {
            Timestamp = DateTime.Now;
        }
    }

    public class SystemStatusEvent : SystemRxn, IRouteAware
    {
        public string SystemName { get; set; }
        public string Ip { get; set; }

        public SystemStatus Status { get; set; }

        public string IpAddress { get; set; }
        
        public string Version { get; set; }
        public bool KeepUpToDate { get; set; }

    }

    public enum SystemStatus
    {
        Ok,
        Error
    }
}
