﻿using System;
using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface IUserContext
    {
        Guid Id { get; }
        string UserName { get; }
        string Tenant { get; }
        string[] Roles { get; }
        string Name { get; }
        string Email { get; }
       // Lazy<RxnApp> Client { get; }
        IEnumerable<IDomainEvent> SaveChanges();
    }
}
