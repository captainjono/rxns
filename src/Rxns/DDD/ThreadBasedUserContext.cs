using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Tenant;

namespace Rxns.DDD
{


    public class ThreadBasedUserContext : IUserContext
    {
        //private readonly IClientModelRepository _appRepo;
        private readonly IPrincipal _user;

        public string Email
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return null;

                var claims = new ClaimsIdentity(GetUser().Identity);
                return claims.FindFirst(RxnClaimTypes.Email).Value;
            }
        }
        public string Name
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return "anonymous";

                var claims = new ClaimsIdentity(GetUser().Identity);
                return claims.FindFirst(RxnClaimTypes.FullName).Value;
            }
        }

        //public Lazy<RxnAgg> Client { get; set; }

        public Guid Id
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return Guid.Empty;

                var claims = new ClaimsIdentity(GetUser().Identity);
                return Guid.Parse(claims.FindFirst(RxnClaimTypes.UserId).Value);
            }
        }

        public string UserName
        {
            get
            {
                return GetUser().Identity.Name;
            }
        }

        public string Tenant
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return null;

                var claims = new ClaimsIdentity(user.Identity);
                return claims.FindFirst(RxnClaimTypes.Tenant).Value;
            }
        }

        public string[] Roles
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return null;

                var claims = new ClaimsIdentity(user.Identity);
                return claims.FindAll(RxnClaimTypes.Role).Select(s => s.Value).ToArray();
            }
        }


        public IPrincipal GetUser()
        {
            var principal = _user;
            if (principal == null) throw new UnauthorizedAccessException("Authentication with system has not been established");

            return principal;
        }

        public ThreadBasedUserContext()//IClientModelRepository appRepo = null)
        {
            _user = Thread.CurrentPrincipal;
            //_appRepo = appRepo;

            //Client = new Lazy<RxnAgg>(() =>
            //{
            //    if (appRepo == null)
            //    {
            //        ReportsStatusLogging.Log.OnError("AppRepo is null, do not use this overload");
            //        return null;
            //    }
            //    return appRepo.GetById(Tenant, UserName);
            //});
        }

        public ThreadBasedUserContext(IPrincipal threadContext)
        {
            _user = threadContext;
        }

        public IEnumerable<IDomainEvent> SaveChanges()
        {
            //if (_appRepo == null)
            //{
            //    ReportStatus.Log.OnError("AppRepo is null, do not use this overload");
            //    return null;
            //}
            //return _appRepo.Save(Tenant, Client.Value);

            return new IDomainEvent[0];
        }
    }
}
