using System;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface ICurrentUsersService
    {
        IObservable<string[]> CurrentUsers(string tenant);
    }
}
