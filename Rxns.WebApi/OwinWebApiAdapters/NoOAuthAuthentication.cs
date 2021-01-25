using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns.WebApi
{
    public class RxnClaimTypes
    {
        public static string Tenant = "rxn.tenant";
        public static string FullName = "rxn.fullname";
        public static string Role = "rxn.role";
    }

    public class NoAuthenticationWithExpiration : NoOAuthAuthentication
    {
        private readonly TimeSpan _tokenTtl;

        public NoAuthenticationWithExpiration(string userName, string tenant, string role, TimeSpan tokenTtl)
            : base(userName, tenant, role)
        {
            _tokenTtl = tokenTtl;
        }

        public override Task TokenEndpoint(OAuthTokenEndpointContext context)
        {
            OnInformation("Giving '{0}' token to '{1}' expiring in {1}", context.Identity.AuthenticationType, context.Identity.Name, _tokenTtl);
            context.Properties.ExpiresUtc = DateTime.UtcNow + _tokenTtl;

            return Task.FromResult<object>(null);
        }

    }

    public class NoOAuthAuthentication : OAuthAuthorizationServerProvider, IReportStatus
    {
        private readonly ReportsStatus _rsImpl = new ReportsStatus(typeof(NoOAuthAuthentication).Name);
        public readonly string UserName;
        public readonly string Tenant;
        public readonly string Role;

        public NoOAuthAuthentication() : this("test", "testtenant", "Admin")
        {

        }

        public NoOAuthAuthentication(string userName, string tenant, string role)
        {
            UserName = userName;
            Tenant = tenant;
            Role = role;
        }

        public override Task ValidateClientAuthentication(OAuthValidateClientAuthenticationContext context)
        {
            context.OwinContext.Response.Headers.AddOrReplace("Access-Control-Allow-Origin", new[] { "*" });
            context.OwinContext.Set("tenant", context.Parameters["tenant"] ?? "test");
            context.Validated();

            return Task.FromResult<object>(null);
        }
        
        public override Task GrantResourceOwnerCredentials(OAuthGrantResourceOwnerCredentialsContext context)
        {
            return this.ReportExceptions(() =>
            {
                context.OwinContext.Response.Headers.AddOrReplace("Access-Control-Allow-Origin", new[] { "*" });

                OnVerbose("Authenticating '{0}'", context.UserName);

                var identity = GetClaimsForUser(context.Options.AuthenticationType, UserName, Tenant, Role);
                
                var props = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        { "as:client_id", (context.ClientId == null) ? string.Empty : context.ClientId },
                        { ".userName", context.UserName },
                    });
                
                var ticket = new AuthenticationTicket(identity, props);
                context.Validated(ticket);
                return Task.FromResult<object>(null);
            },
            error =>
            {
                var c = context;
                c.SetError("Login failed");
                c.Rejected();
                return Task.FromResult<object>(null);
            });
        }

        protected ClaimsIdentity GetClaimsForUser(string authType, string userName, string tenant, string role)
        {
            var identity = new ClaimsIdentity(authType);

            identity.AddClaim(new Claim(ClaimTypes.Name, userName));
            identity.AddClaim(new Claim(RxnClaimTypes.Tenant, tenant));
            identity.AddClaim(new Claim(RxnClaimTypes.FullName, userName));
            identity.AddClaim(new Claim(RxnClaimTypes.Role, role));

            return identity;
        }


        public override Task TokenEndpoint(OAuthTokenEndpointContext context)
        {
            //this exposes the additional properties to the client
            foreach (KeyValuePair<string, string> property in context.Properties.Dictionary)
            {
                context.AdditionalResponseParameters.Add(property.Key, property.Value);
            }

            return base.TokenEndpoint(context);
        }

        public IObservable<LogMessage<Exception>> Errors
        {
            get { return _rsImpl.Errors; }
        }

        public IObservable<LogMessage<string>> Information
        {
            get { return _rsImpl.Information; }
        }

        public string ReporterName
        {
            get { return GetType().Name; }
        }

        public void OnError(Exception exception)
        {
            _rsImpl.OnError(exception);
        }

        public void OnError(string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(exceptionMessage, args);
        }

        public void OnError(Exception innerException, string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(innerException, exceptionMessage, args);
        }

        public void OnInformation(string info, params object[] args)
        {
            _rsImpl.OnInformation(info, args);
        }

        public void OnWarning(string info, params object[] args)
        {
            _rsImpl.OnWarning(info, args);
        }

        public void OnVerbose(string info, params object[] args)
        {
            _rsImpl.OnVerbose(info, args);
        }

        public void OnDispose(IDisposable me)
        {
            _rsImpl.OnDispose(me);
        }


        public void Dispose()
        {
            _rsImpl.Dispose();
        }
    }
}
