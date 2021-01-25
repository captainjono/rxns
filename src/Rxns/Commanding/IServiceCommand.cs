using System;
using Rxns.Cloud;
using Rxns.Interfaces;

namespace Rxns.DDD.Commanding
{    
    /// <summary>
    /// A command which interacts with an application service outside
    /// the context of any buisness domain. The main idea behind a this style of 
    /// command is that it should be constructable easily via a text expression
    /// </summary>
    public interface IServiceCommand : IUniqueRxn
    {
        /// <summary>
        /// A string parsable representation of this command
        /// </summary>
        /// <returns></returns>
        string ToString();
    }

    public static class ServiceCommandExtensions
    {
        public static RxnQuestion AsQuestion(this IUniqueRxn cmd)
        {
            return new RxnQuestion()
            {
                Options = cmd.ToString(),
                Id = cmd.Id
            };
        }
    }

    public class RxnQuestion : IUniqueRxn
    {
        public string Options { get; set; }
        public string Id { get; set; }
        public string Destination { get; set; }
        public string From { get; set; }
        public string Command { get; set; }

        public static T ForTenant<T>(string tenant, string systemName, string service = "") where T : RxnQuestion, new()
        {
            return new T()
            {
                Destination = RouteExtensions.GetRoute(tenant, systemName, service)
            };
        }

        public override string ToString()
        {
            return String.Format("[{0}]{1}:{2}", Destination, Command, Options);
        }

    }

    public static class RemoteCommandExtensions
    {
        public static string AsRootRoute(this string routestring)
        {
            var components = routestring.Split('\\');
            return String.Format("{0}\\{1}", components[0], components[1]).AsRoute();
        }

        /// <summary>
        /// Determins if this command is for the given tenant and/or system
        /// </summary>
        /// <param name="cmd">The command</param>
        /// <param name="route">The tenant, systemName, or tenant\systenName, or tenant\systemName\app</param>
        /// <returns>The the command is intended for the given tenant or system</returns>
        public static bool IsFor(this RxnQuestion cmd, string route)
        {
            return String.IsNullOrWhiteSpace(cmd.Destination) || cmd.Destination.AsRoute().Contains(route.AsRoute());
        }

        /// <summary>
        /// Determins if this status message is for the given tenant and/or system
        /// </summary>
        /// <param name="cmd">The command</param>
        /// <param name="route">The tenant, systemName, or tenant\systenName, or tenant\systemName\app</param>
        /// <returns>The the command is intended for the given tenant or system</returns>
        public static bool IsFor(this SystemStatusEvent status, string route)
        {
            return RxnQuestion.ForTenant<RxnQuestion>(status.Tenant, status.SystemName).Destination.AsRoute().Contains(route.AsRoute());
        }


        public static string AsRoute(this string route)
        {
            return route.ToLower().Replace(" ", "");
        }
    }
}
