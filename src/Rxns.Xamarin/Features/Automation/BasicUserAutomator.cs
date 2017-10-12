using System;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Xamarin.Features.Automation.PlayBackFilter;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Automation
{
    public class BasicUserAutomator : IAutomateUserActions
    {
        public IObservable<bool> AutomateUserActions(Page page, RxnPageModel model, IObservable<IRxn> actions, Action<IRxn> publish)
        {
            return new BehaviorSubject<bool>(true);
        }

        public ITapePlaybackFilter[] Filters
        {
            get { return new ITapePlaybackFilter[] { new UserActionsOnlyFilter() }; }
        }
    }
}
