using System;
using Rxns.Logging;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    /// <summary>
    /// Knows how to generate a navigation event to get to where you want! Can be sent
    /// over the wire, fully serilisable.
    /// This event is consumed by the navigation orchestractor
    /// </summary>
    public class NavigationEventGenerator : ReportsStatus, INavigationService<IRxnPageModel>
    {
        private readonly IAppNav<Page, IRxnPageModel> _appNav;

        public string CurrentPage { get { return _appNav.Current == null ? "(NULL)" : _appNav.Current.Model.GetType().Name; } }
        public string PreviousPage { get { return _appNav.Previous.Count == 0 || _appNav.Previous.Peek() != null ? "(NULL)" : _appNav.Previous.Peek().Model.GetType().Name; } }

        public NavigationEventGenerator(IAppNav<Page, IRxnPageModel> appNav)
        {
            _appNav = appNav;
        }

        public NavigationAction Pop(bool modal = false)
        {
            OnVerbose("Generating action to navigate from {0} to {1}", CurrentPage, PreviousPage);
            return new NavigationAction()
            {
                IsPushing = false,
                IsModal = modal
            };
        }

        public NavigationAction PopTo<TPageModel>(bool modal = false) 
            where TPageModel : IRxnPageModel, IViewModel
        {
            OnVerbose("Generating action to navigate from {0} to {1}", CurrentPage, PreviousPage);
            return new NavigationAction()
            {
                IsPoppingTo = true,
                IsPushing = false,
                IsModal = modal,
                PageModelType = typeof(TPageModel)
            };
        }

        public NavigationAction PopTo(Type pageModel, bool model = false)
        {
            OnVerbose("Generating action to navigate from {0} to {1}", CurrentPage, PreviousPage);
            return new NavigationAction()
            {
                IsPoppingTo = true,
                IsPushing = false,
                IsModal = model,
                PageModelType = pageModel
            };
        }

        public NavigationAction Push<TNextPageModel, TCfg>(TCfg cfg, bool modal = false)
            where TNextPageModel : IRxnPageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl
        {
            OnVerbose("Generating action to navigate from {0} to {1}", CurrentPage, typeof(TNextPageModel).Name);

            return new NavigationAction()
            {
                IsPushing = true,
                Cfg = cfg,
                IsModal = modal,
                PageModelType = typeof(TNextPageModel)
            };
        }

        public NavigationAction Push<TNextPage, TNextPageModel, TCfg>(TCfg cfg, bool modal = false)
            where TNextPage : Page
            where TNextPageModel : IRxnPageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl
        {
            OnVerbose("Generating action to navigate from {0} to {1}({2})", CurrentPage, typeof(TNextPage).Name, typeof(TNextPageModel).Name);

            return new NavigationAction()
            {
                IsPushing = true,
                IsModal = modal,
                Cfg = cfg,
                PageModelType = typeof(TNextPageModel),
                PageType = typeof(TNextPage)
            };
        }

        public NavigationAction Swap<TNextPageModel, TCfg>(TCfg cfg, bool showNavBar = false)
            where TNextPageModel : IRxnPageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl
        {
            OnVerbose("Generating action to swap page {0} with {1}", CurrentPage, typeof(TNextPageModel).Name);

            return new NavigationAction()
            {
                IsPushing = true,
                IsSwapping = true,
                Cfg = cfg,
                ShowNav = showNavBar,
                PageModelType = typeof(TNextPageModel)
            };
        }

        public NavigationAction Swap<TNextPage, TNextPageModel, TCfg>(TCfg cfg, bool showNavBar = false)
            where TNextPage : Page
            where TNextPageModel : IRxnPageModel, IViewModelWithCfg<TCfg>
            where TCfg : ICfgFromUrl
        {
            OnVerbose("Generating action to swap page {0} with {1}", CurrentPage, typeof(TNextPageModel).Name);

            return new NavigationAction()
            {
                IsSwapping = true,
                ShowNav = showNavBar,
                Cfg = cfg,
                PageModelType = typeof(TNextPageModel),
                PageType = typeof(TNextPage)
            };
        }

        public NavigationAction Push<TNextPageModel>(bool modal = false) where TNextPageModel : IRxnPageModel, IViewModel
        {
            OnVerbose("Generating action to navigate from {0} to {1}", CurrentPage, typeof(TNextPageModel).Name);

            return new NavigationAction()
            {
                IsPushing = true,
                IsModal = modal,
                PageModelType = typeof(TNextPageModel)
            };
        }
    }
}

