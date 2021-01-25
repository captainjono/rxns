using System;
using System.Net.Http;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Interfaces.Reliability;

namespace Rxns.DDD.CQRS
{
    public class AuthenticatedHttpConnection : AnonymousHttpConnection
    {
        private readonly HttpClient _client;
        private readonly IUserAuthenticationService _authenticationService;
        private readonly object _singleThread = new object();

        public AuthenticatedHttpConnection(HttpClient client, IUserAuthenticationService authenticationService, IReliabilityManager reliabilityManager) : base(client, reliabilityManager)
        {
            _client = client;
            _authenticationService = authenticationService;
        }

        public override IObservable<HttpClient> GetClient()
        {
            return Rxn.Create<HttpClient>(client =>
            {
                return _authenticationService.Tokens.FirstAsync().Subscribe(token =>
                    {
                        try
                        {
                            lock (_singleThread)
                            {
                                if (_client.DefaultRequestHeaders.Authorization == null || _client.DefaultRequestHeaders.Authorization.Parameter != token.Token)
                                {
                                    _client.DefaultRequestHeaders.Authorization = token.ToAuthorizationHeader();
                                }
                            }

                            client.OnNext(_client);
                            client.OnCompleted();
                        }
                        catch (Exception e)
                        {
                            client.OnError(e);
                        }
                    },
                    error => client.OnError(error));
            });
        }
    }
}
