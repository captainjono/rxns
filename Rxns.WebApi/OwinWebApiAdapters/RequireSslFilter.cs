using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace Rxns.WebApi
{
    /// <summary>
    /// Forces the connection to require ssl to be used on the transport layer
    /// except for when requests arrive on the localhost loopback
    /// </summary>
    public class RequireSslFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext.Request.RequestUri.IsLoopback)
                return;

            if (actionContext.Request.RequestUri.Scheme != Uri.UriSchemeHttps)
            {
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    ReasonPhrase = "SSL is required to access this feature"
                };
            }
        }
    }
}
