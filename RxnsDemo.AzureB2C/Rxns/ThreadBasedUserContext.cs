using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Rxns;
using Rxns.DDD.BoundedContext;
using Rxns.Logging;

namespace RxnsDemo.AzureB2C.Rxns
{


    public class ThreadBasedRvUserContext : IUserContext
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
                return claims.FindFirst(RvClaimTypes.Email).Value;
            }
        }
        public string Name
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return "anonymous";

                var claims = new ClaimsIdentity(GetUser().Identity);
                return claims.FindFirst(RvClaimTypes.FullName).Value;
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
                return Guid.Parse(claims.FindFirst(RvClaimTypes.UserId).Value);
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
                return claims.FindFirst(RvClaimTypes.Tenant).Value;
            }
        }

        public string[] Roles
        {
            get
            {
                var user = GetUser();
                if (!user.Identity.IsAuthenticated) return null;

                var claims = new ClaimsIdentity(user.Identity);
                return claims.FindAll(RvClaimTypes.Role).Select(s => s.Value).ToArray();
            }
        }


        public IPrincipal GetUser()
        {
            var principal = _user;
            if (principal == null) throw new UnauthorizedAccessException("Authentication with system has not been established");

            return principal;
        }

        public ThreadBasedRvUserContext()//IClientModelRepository appRepo = null)
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

        public ThreadBasedRvUserContext(IPrincipal threadContext)
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
