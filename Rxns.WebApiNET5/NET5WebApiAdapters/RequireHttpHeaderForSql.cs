//using System.Net.Http;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Rxns.WebApi
//{
//    /// <summary>
//    /// This class injects a strict-transport-security header into the request pipeline
//    /// to ensure that clients using a ssl connection to access the service. This should be
//    /// used as a backup security precaution
//    /// </summary>
//    public class RequireHttpHeaderForSsl : DelegatingHandler
//    {
//        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
//        {
//            if (request.IsLocal())
//                request.Headers.Add("Strict-Transport-Security", "max-age=16070400; includeSubDomains");

//            return base.SendAsync(request, cancellationToken);
//        }
//    }
//}
