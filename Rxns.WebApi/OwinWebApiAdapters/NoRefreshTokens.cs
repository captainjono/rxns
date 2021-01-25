using System;
using System.Threading.Tasks;
using Microsoft.Owin.Security.Infrastructure;

namespace Rxns.WebApi
{
    public class NoRefreshTokens : IAuthenticationTokenProvider
    {

        public NoRefreshTokens()
        {
        }

        public Task CreateAsync(AuthenticationTokenCreateContext context)
        {

            return Task.FromResult<object>(null);
        }

        public Task ReceiveAsync(AuthenticationTokenReceiveContext context)
        {

            return Task.FromResult<object>(null);
        }

        public void Create(AuthenticationTokenCreateContext context)
        {
            throw new NotImplementedException();
        }

        public void Receive(AuthenticationTokenReceiveContext context)
        {
            throw new NotImplementedException();
        }
    }
}
