using System;
using System.Collections.Generic;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantContext
    {
        //Lazy<TenantSettings> Cfg { get; }
        string Tenant { get; }
        
        ITenantDatabaseFactory DatabaseContext { get; }
        ITenantDiscardRepository DiscardContext { get; }
        
        
        //IDictionary<string, Document> Documents { get; }
        //Users { get; }
    }
}
