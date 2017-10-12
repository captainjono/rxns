using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Composition
{
    /// <summary>
    /// A defining of the pages that are central to the app.
    /// 
    /// The app support two modes, a pre-authentication mode and a post-authentication mode
    /// The app also support settings, which are presented to the user to allow them to configure the app
    /// </summary>
    public interface IAppPages
    {
        /// <summary>
        /// The login page that is displayed when the app is first started in the
        /// pre-authentication stage. A UserLoggedIn action should be triggered by the app
        /// when authentication is successful, trigging the mainPage to be displayed. Consequenetly,
        /// UserLoggedOut action will trigger the loginPage to be displayed again
        /// </summary>
        /// <returns></returns>
        Page LoginPage();
        
        /// <summary>
        /// The page displayed after UserLoggedIn action is observed. This is the main
        /// page of the application
        /// </summary>
        /// <returns></returns>
        Page MainPage();

        /// <summary>
        /// The page which is displayed immediataly after the app has started in order to show
        /// a responsive UI. It is then upto the implementor to at some stage load what it needs and then 
        /// show the LoginPage or the MainPage.
        /// </summary>
        /// <returns></returns>
        Page SplashPage();
        /// <summary>
        /// sets up whether the first page has a navigation bar when the app is opened
        /// </summary>
        bool MainPageHasNav { get; }
    }
}
