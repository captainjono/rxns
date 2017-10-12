using System;
using System.Reactive;
using Rxns.Interfaces;
using Rxns.Xamarin.Features.Composition;
using Rxns.Xamarin.Features.UserDomain;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public interface INavigationOrchestrator : IRxnProcessor<NavigationAction>, 
                                                IRxnProcessor<UserLoggedIn>, 
                                                IRxnProcessor<UserLoggingOut>, 
                                                IRxnProcessor<EventPublishingOsBridge.AppResumed>, 
                                                IRxnProcessor<EventPublishingOsBridge.AppBackgrounded>
    {
        IObservable<Unit> ShowPage(Page page);
        IObservable<Unit> ShowPageModal(Page page);
        IObservable<Unit> HideCurrentPage();
        IObservable<Unit> HideCurrentPageModal();
    }
}
