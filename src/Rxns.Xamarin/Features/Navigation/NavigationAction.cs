using System;
using Rxns.Xamarin.Features.UserDomain;

namespace Rxns.Xamarin.Features.Navigation
{
    public class NavigationAction : IAction
    {
        /// <summary>
        /// The page this action relates too
        /// </summary>
        public Type PageModelType { get; set; }
        /// <summary>
        /// If the page should be displayed as modal
        /// </summary>
        public bool IsModal { get; set; }
        /// <summary>
        /// If this is the next page or previous
        /// </summary>
        public bool IsPushing { get; set; }
        /// <summary>
        /// Replaces the current root page with the next, destroying any stack
        /// </summary>
        public bool IsSwapping { get; set; }
        /// <summary>
        /// When swapping a page, the root wont have a nav bar. This
        /// option forces a nav bar on the root page
        /// </summary>
        public bool ShowNav { get; set; }
        /// <summary>
        /// If the pageType implements IViewModelWithCfg<TCfg/>, this is supposed
        /// to be that configuration object or null
        /// </summary>
        public object Cfg { get; set; }
        /// <summary>
        /// The type of the page that will be displayed with the model
        /// This can be null, and the page will be inferred by using PageModelType
        /// and convention
        /// PageModel = SomePageModel
        /// Page = SomePage
        /// </summary>
        public Type PageType { get; set; }
        /// <summary>
        /// If the page is calling the PoppingTo function
        /// </summary>
        public bool IsPoppingTo { get; set; }
    }
}

