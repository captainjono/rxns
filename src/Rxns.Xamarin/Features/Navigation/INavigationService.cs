using System;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public interface INavigationService<in TBasePageModel>
    {
        /// <summary>
        /// Goes back to the previous page in the stack
        /// </summary>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Pop(bool modal = false);
        /// <summary>
        /// Pops the stack until a specified pageModel is reached
        /// </summary>
        /// <param name="animate"></param>
        /// <returns></returns>
        NavigationAction PopTo<TPreviousPageModel>(bool model = false) 
            where TPreviousPageModel : TBasePageModel, IViewModel;

        NavigationAction PopTo(Type pageModel, bool model = false);
        /// <summary>
        /// The basic method of navigating to another page.
        /// It uses the convention of pageIWant{Page} -> pageIwant{PageModel}
        /// ie. Push{PageIWantPageModel}
        /// So u dont need to specify the pageModel u want to navigate 
        /// if you follow this convention
        /// </summary>
        /// <typeparam name="TNextPageModel"></typeparam>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Push<TNextPageModel>(bool modal = false) 
            where TNextPageModel : TBasePageModel, IViewModel;

        /// <summary>
        /// Load a page, with a given configuration, using the convention approach described previously.
        /// </summary>
        /// <typeparam name="TNextPageModel">The pageModel u want to navigate to</typeparam>
        /// <typeparam name="TCfg">The configuration u want to load into the pageModel</typeparam>
        /// <param name="cfg"></param>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Push<TNextPageModel, TCfg>(TCfg cfg, bool modal = false)
            where TNextPageModel : TBasePageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl;

        /// <summary>
        /// The most flexible navigation method, lets u specify the page, pageModel and Cfg u want to 
        /// load when going somewhere.
        /// </summary>
        /// <typeparam name="TNextPage"></typeparam>
        /// <typeparam name="TNextPageModel"></typeparam>
        /// <typeparam name="TCfg"></typeparam>
        /// <param name="cfg"></param>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Push<TNextPage, TNextPageModel, TCfg>(TCfg cfg, bool modal = false)
            where TNextPageModel : TBasePageModel, IViewModelWithCfg<TCfg>
            where TNextPage : Page
            where TCfg : ICfgFromUrl;

        /// <summary>
        /// Swaps a page as the new root, with a given configuration, using the convention approach described previously.
        /// </summary>
        /// <typeparam name="TNextPageModel">The pageModel u want to navigate to</typeparam>
        /// <typeparam name="TCfg">The configuration u want to load into the pageModel</typeparam>
        /// <param name="cfg"></param>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Swap<TNextPageModel, TCfg>(TCfg cfg, bool showNavBar = false)
            where TNextPageModel : TBasePageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl;

        /// <summary>
        /// The most flexible navigation method, lets u specify the root page, pageModel and Cfg u want to 
        /// load when going somewhere.
        /// </summary>
        /// <typeparam name="TNextPage"></typeparam>
        /// <typeparam name="TNextPageModel"></typeparam>
        /// <typeparam name="TCfg"></typeparam>
        /// <param name="cfg"></param>
        /// <param name="modal"></param>
        /// <returns></returns>
        NavigationAction Swap<TNextPage, TNextPageModel, TCfg>(TCfg cfg, bool showNavBar = false)
            where TNextPageModel : TBasePageModel, IViewModelWithCfg<TCfg>
            where TNextPage : Page
            where TCfg : ICfgFromUrl;
    }
}

