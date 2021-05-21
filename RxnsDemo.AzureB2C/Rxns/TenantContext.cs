using System;
using System.Collections.Generic;
using Rxns.DDD.BoundedContext;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.Rxns
{

    public class Document
    {
        public string Id { get; set; }
    }

    public class MyOrganisation
    {

    }

    public class TenantContext : ITenantContext
    {
        //public Lazy<TenantSettings> Cfg { get; private set; }
        public string Tenant { get; private set; }
        public ITenantDatabaseFactory DatabaseContext { get; private set; }
        public ITenantDiscardRepository DiscardContext { get; private set; }
        public ICurrentUsersService Users { get; set; }

        
        public IDictionary<string, Document> Documents { get; private set; }
        public MyOrganisation Org { get; private set; }
        public TenantContext(string tenant, ITenantDatabaseFactory dbFactory, ICurrentUsersService users, ITenantDiscardRepository DiscardRepo)
        {
            Tenant = tenant;
            DatabaseContext = dbFactory;
            Users = users;
            DiscardContext = DiscardRepo;
          //  Cfg = new Lazy<TenantSettings>(() => new TenantSettings(cfgProvider(tenant)));
        }

    }
}