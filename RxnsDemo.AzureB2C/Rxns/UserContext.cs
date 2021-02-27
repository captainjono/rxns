using System;
using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class UserContext : IUserContext
    {
        private readonly ITenantUserContext _context;
        //private readonly ITenantModelRepository<RxnAgg> _appRepo;
        //public Lazy<RxnAgg> Client { get; private set; }
        public string Tenant { get; private set; }
        public string UserName { get; private set; }
        public string[] Roles { get { return _context.GetRoles(Id); } }

        private string _email;
        public string Email { get { return _email ?? (_email = _context.GetEmail(Id)); } }
        private string _name;
        public string Name { get { return _name ?? (_name = _context.GetName(Id)); } }
        private Guid? _id;
        public Guid Id { get { return _id ?? (_id = _context.GetUserId(UserName)).Value; } }
        
        public UserContext(string tenant, string userName, ITenantUserContext context)//, IClientModelRepository appRepo = null)
        {
            Tenant = tenant;
            UserName = userName;
            _context = context;
            //_appRepo = appRepo;

            //Client = new Lazy<RxnApp>(() =>
            //{
            //    if (appRepo == null)
            //    {
            //        ReportStatus.Log.OnError("AppRepo is null, do not use this overload");
            //        return null;
            //    }
            //    return appRepo.GetById(Tenant, UserName);
            //});
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
