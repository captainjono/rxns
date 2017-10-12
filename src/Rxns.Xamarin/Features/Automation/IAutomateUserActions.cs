using System;
using Rxns.Interfaces;
using Rxns.Xamarin.Features.Automation.PlayBackFilter;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Automation
{
    /// <summary>
    /// This interface defines a service which automates the user input on a screen. The input is a series of
    /// actions that should be translates by this service into something happening on a page.
    /// </summary>
    public interface IAutomateUserActions
    {
        /// <summary>
        /// This is used to start the automation process of the given page. The observable should be hot until
        /// the caller disposes of it, signifiying it no longer needs the page automated.
        /// </summary>
        /// <param name="page"></param>
        /// <param name="model"></param>
        /// <param name="actions">The stream of user actions to automate</param>
        /// <param name="publish"></param>
        /// <returns></returns>
        IObservable<bool> AutomateUserActions(Page page, RxnPageModel model, IObservable<IRxn> actions, Action<IRxn> publish);

        /// <summary>
        /// The filters that will modify the event playback to work with this
        /// automation service properly
        /// </summary>
        ITapePlaybackFilter[] Filters { get; }
    }
}
