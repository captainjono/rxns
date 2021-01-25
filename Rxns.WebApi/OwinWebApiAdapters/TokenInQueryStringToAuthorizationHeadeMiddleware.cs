using System.Threading.Tasks;
using Microsoft.Owin;

namespace Rxns.WebApi
{
    public class TokenInQueryStringToAuthorizationHeaderMiddleware : OwinMiddleware
    {
        public TokenInQueryStringToAuthorizationHeaderMiddleware(OwinMiddleware next)
            : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (context.Request.Path.Value.StartsWith("/negotiate"))
            {
                string bearerToken = context.Request.Query.Get("bearer_token");
                if (bearerToken != null)
                {
                    string[] authorization = { "Bearer " + bearerToken };
                    context.Request.Headers.Add("Authorization", authorization);
                }
            }

            await Next.Invoke(context);
        }
    }
}
