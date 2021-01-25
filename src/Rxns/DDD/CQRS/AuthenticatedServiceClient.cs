using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.DDD.CQRS
{
    public abstract class AuthenticatedServiceClient : ReportsStatus
    {
        protected IHttpConnection Connection;
        protected string BaseUrl { get; set; }

        protected AuthenticatedServiceClient(IHttpConnection connection)
        {
            Connection = connection;
        }

        /// <summary>
        /// Appends the base url to a string as well
        /// as normalising the URL for http requests, ie. removing spaces
        /// </summary>
        /// <param name="route"></param>
        /// <returns></returns>
        protected string WithBaseUrl(string route)
        {
            return string.Format("{0}/{1}", BaseUrl, route).Replace(" ", "");
        }
    }
}
