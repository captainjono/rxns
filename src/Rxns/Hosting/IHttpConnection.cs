using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.Hosting
{
    public interface IHttpConnection
    {
        IObservable<HttpClient> GetClient();
        IObservable<HttpResponseMessage> Call(Func<HttpClient, Task<HttpResponseMessage>> request);
        IObservable<HttpResponseMessage> Call(Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> request);
    }
}
