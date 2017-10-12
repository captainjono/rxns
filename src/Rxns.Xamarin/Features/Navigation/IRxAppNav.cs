using System;

namespace Rxns.Xamarin.Features.Navigation
{
    public interface IRxAppNav<TBasePage, TBasePageModel> : IAppNav<TBasePage, TBasePageModel>
    {
        IObservable<AppPageInfo<TBasePage, TBasePageModel>> CurrentView { get; }
    }
}
