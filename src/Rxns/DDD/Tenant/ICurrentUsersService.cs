using System;

namespace Rxns.DDD.Tenant
{
    public interface ICurrentUsersService
    {
        IObservable<string[]> CurrentUsers(string tenant);
    }
}
