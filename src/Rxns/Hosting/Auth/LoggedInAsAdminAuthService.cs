using System;
using System.Reactive;
using Rxns.Hosting.Auth;

namespace Rxns.Hosting
{
    public class AlreadyLoggedInAsAdminAuthService : IUserAuthenticationService
    {
        public IObservable<bool> IsAuthenticated => true.ToObservable();
        public IObservable<UserAccessToken> Login(IUserCredentials credentials)
        {
            return new UserAccessToken()
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Role = "Admin",
                Expires = DateTime.Now.AddDays(100),
                Issued = DateTime.Now
            }.ToObservable();
        }

        public IObservable<UserAccessToken> Tokens => Login(new RxnServiceInfo());

        public void Logout()
        {

        }
    }
}
