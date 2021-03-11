﻿using System;
using System.Threading;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class UserContextFactory : IUserContextFactory
    {
        private readonly ITenantDatabaseFactory _dbFactory;

        public UserContextFactory(ITenantDatabaseFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public IUserContext GetUserContext(string tenant, string userName = null)
        {
            return Thread.CurrentPrincipal != null && (Thread.CurrentPrincipal.Identity?.Name == userName || userName.IsNullOrWhitespace())
                ? (IUserContext)new ThreadBasedUserContext() : 
                new UserContext(tenant, userName, _dbFactory.GetUsersContext(_dbFactory.GetContext(tenant)));
        }
    }
}
