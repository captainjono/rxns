using System;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public interface IResolvePages
    {
        /// <summary>
        /// Resolves view model with a given  configuration, ready for use
        /// </summary>
        /// <typeparam name="TPageModel"></typeparam>
        /// <returns></returns>
        TPageModel ResolvePageModel<TPageModel>() where TPageModel : IRxnPageModel;

        Page ResolvePageWithModel<TPageModel>() where TPageModel : IRxnPageModel;

        IRxnPageModel ResolvePageModel(Type pageModelType, object vmcfg = null);

        Page ResolvePageWithModel(Type pageModelType, object vmcfg = null);

        
        TPageModel ResolvePageModel<TPageModel, TViewModelCfg>(TViewModelCfg cfg)
            where TPageModel : IRxnPageModel, IViewModelWithCfg<TViewModelCfg>
            where TViewModelCfg : ICfgFromUrl;

        Page ResolvePageWithModel<TPageModel, TViewModelCfg>(TViewModelCfg cfg)
            where TPageModel : IRxnPageModel, IViewModelWithCfg<TViewModelCfg>
            where TViewModelCfg : ICfgFromUrl;

        Page ResolvePageWithModel(Type pageType, IRxnPageModel pageModel, object vmcfg = null, bool boostrapPage = true, bool bootstrapModel = true);

        TPage ResolvePageWithModel<TPage>(IRxnPageModel pageModel, object vmCfg, bool shouldBootstrapPage = true, bool shouldBootstrapModel = true) 
            where TPage : Page;
    }
}
