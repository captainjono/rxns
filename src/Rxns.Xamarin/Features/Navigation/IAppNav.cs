using System.Collections.Generic;

namespace Rxns.Xamarin.Features.Navigation
{
    /// <summary>
    /// A virtual navigation stack for the app
    /// </summary>
    public interface IAppNav<TBasePage, TBasePageModel>
    {
        /// <summary>
        /// The current page
        /// </summary>
        AppPageInfo<TBasePage, TBasePageModel> Current { get; }
        /// <summary>
        /// The page before the current page
        /// </summary>
        Stack<AppPageInfo<TBasePage, TBasePageModel>> Previous { get; }

        /// <summary>
        /// Navigates to the next page
        /// </summary>
        /// <param name="next"></param>
        void Push(TBasePage nextPage, TBasePageModel nextModel);
        /// <summary>
        /// navigates from a page
        /// </summary>
        void Pop();

        void PushRoot(TBasePage page, TBasePageModel nextModel);
    }
}
