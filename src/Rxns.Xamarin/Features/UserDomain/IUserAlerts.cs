using System;
using System.Reactive;

namespace Rxns.Xamarin.Features.UserDomain
{
    public interface IUserAlerts
    {
        IObservable<Unit> ShowLoading(string message);
        IObservable<Unit> HideLoading();
    }
}
