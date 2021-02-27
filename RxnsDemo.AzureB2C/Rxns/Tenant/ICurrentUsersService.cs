using System;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ICurrentUsersService
    {
        IObservable<string[]> CurrentUsers(string tenant);
    }
}
