using System;
using System.Net.Http;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Hosting;
using Rxns.Interfaces.Reliability;

namespace Rxns.DDD.CQRS
{
    public class AnonymousHttpConnection : IHttpConnection
    {
        private readonly HttpClient _client;
        private readonly IReliabilityManager _reliabily;

        public AnonymousHttpConnection(HttpClient client, IReliabilityManager reliabily)
        {
            _client = client;
            _reliabily = reliabily;
        }

        public virtual IObservable<HttpClient> GetClient()
        {
            return _client.ToObservable();
        }

        public IObservable<HttpResponseMessage> Call(Func<HttpClient, Task<HttpResponseMessage>> request)
        {
            return Rxn.Create<HttpResponseMessage>(complete =>
            {
                return GetClient().Subscribe(client =>
                {
                    try
                    {
                        _reliabily.CallOverHttp(() => request(client))
                                    .Subscribe(resp =>
                                    {
                                        complete.OnNext(resp);
                                        complete.OnCompleted();
                                    },
                                    error => complete.OnError(error));

                    }
                    catch (Exception e)
                    {
                        complete.OnError(e);
                    }
                },
                error => complete.OnError(error));
            });
        }

        public IObservable<HttpResponseMessage> Call(Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> request)
        {
            return Rxn.Create<HttpResponseMessage>(complete =>
            {
                var cancellation = new CancellationToken();
                var httpClient = GetClient();
                var call = httpClient.Subscribe(client =>
                {
                    try
                    {
                        _reliabily.CallOverHttp(() => request(client, cancellation))
                            .Subscribe(resp =>
                            {
                                complete.OnNext(resp);
                                complete.OnCompleted();
                            },
                            error => complete.OnError(error));

                    }
                    catch (Exception e)
                    {
                        complete.OnError(e);
                    }
                },
                error => complete.OnError(error));

                cancellation.Register(() =>
                {
                    complete.OnError(new TaskCanceledException());
                });

                return call;
            });
        }
    }




}
