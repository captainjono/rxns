using System;
using System.Linq;
using Autofac;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Xamarin.Features.Navigation;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Autofac
{

    /// <summary>
    /// A page/model resolver written for autofac, which also hooks up any reactions that the
    /// pages and their models implement
    /// </summary>
    public class AutoFacPageResolver : ReportsStatus, IResolvePages
    {
        private readonly ILifetimeScope _createLifeTime;
        private readonly IManageReactors _reactorManager;

        public AutoFacPageResolver(ILifetimeScope createLifeTime, IManageReactors reactorManager)
        {
            _createLifeTime = createLifeTime;
            _reactorManager = reactorManager;
        }

        public TPageModel ResolvePageModel<TPageModel>() where TPageModel : IRxnPageModel
        {
            return (TPageModel) ResolvePageModel(typeof(TPageModel), null);
        }

        public Page ResolvePageWithModel<TPageModel>() where TPageModel : IRxnPageModel
        {
            return ResolvePageWithModel(null, ResolvePageModel<TPageModel>(), null, true, false);
        }
        
        public Page ResolvePageWithModel(Type pageModelType, object vmcfg = null)
        {
            return ResolvePageWithModel(null, ResolvePageModel(pageModelType, vmcfg), vmcfg, true, false);
        }

        public TPageModel ResolvePageModel<TPageModel, TViewModelCfg>(TViewModelCfg cfg) where TPageModel : IRxnPageModel, IViewModelWithCfg<TViewModelCfg> where TViewModelCfg : ICfgFromUrl
        {
            return (TPageModel) ResolvePageModel(typeof (TPageModel), cfg);
        }

        public Page ResolvePageWithModel<TPageModel, TViewModelCfg>(TViewModelCfg cfg) where TPageModel : IRxnPageModel, IViewModelWithCfg<TViewModelCfg> where TViewModelCfg : ICfgFromUrl
        {
            return ResolvePageWithModel(null, ResolvePageModel<TPageModel>(), cfg, true, false);
        }

        public IRxnPageModel ResolvePageModel(Type pageModelType, object vmcfg = null)
        {
            var lifeTime = _createLifeTime.BeginLifetimeScope();

            var pageModel = lifeTime.Resolve(pageModelType) as IRxnPageModel;
            if(pageModel == null) throw new Exception("PageModel must be of type IRxnPageModel");
            pageModel.Disposes(lifeTime);

            BootstrapPageModel(vmcfg, null, pageModel);

            return pageModel;
        }

        public TPage ResolvePageWithModel<TPage>(IRxnPageModel pageModel, object vmCfg = null, bool shouldBootstrapPage = true, bool shouldBootstrapModel = true)
            where TPage : Page
        {
            return (TPage)ResolvePageWithModel(typeof(TPage), pageModel, vmCfg, shouldBootstrapPage, shouldBootstrapModel);
        }
        
        public Page ResolvePageWithModel(Type pageType, IRxnPageModel pageModel, object vmCfg = null, bool shouldBootstrapPage = true, bool shouldBootstrapModel = true) 
        {
            var pt = pageType ?? Type.GetType(GetPageTypeName(pageModel.GetType()));
            if (pt == null)
                throw new Exception(GetPageTypeName(pageModel.GetType()) + " not found");

            var lifetime = _createLifeTime.BeginLifetimeScope();
            var page = (Page)lifetime.Resolve(pt);
            pageModel.Disposes(lifetime);

            BootstrapPageModel(vmCfg, page, pageModel, shouldBootstrapPage, shouldBootstrapModel);

            return page;
        }

        private void BootstrapPageModel(object vmCfg, Page targetPage, IRxnPageModel pageModel, bool connectPage = true, bool connectModel = true)
        {
            Ensure.NotNull(pageModel, "Need to specify a pagemodel to bootstrap");

            OnVerbose("Bootstrapping model {0}", pageModel.GetType().Name);
            if (targetPage != null && targetPage.BindingContext == null)
            {
                targetPage.BindingContext = pageModel;
            }

            if (targetPage != null && connectPage) ConnectToReactors(targetPage, pageModel);

            if (connectModel)
            {
                ConnectToReactors(pageModel, pageModel);

                try
                {
                    ConfigureIfRequired(pageModel, vmCfg);
                    //lets configure the VM with the given cfg if it defines the correct configuration interface
                    pageModel.Create(); //calls show
                }
                catch (Exception e)
                {
                    if (e == null)//this happened once, seriously, with a broken observable
                    {
                        OnError("Catestrophic failure while configuring model!!! {0}", pageModel.GetType());
                        return;
                    }
                    OnError(e);
                }
            }
        }

        private void ConfigureIfRequired(IRxnPageModel pageModel, object vmCfg)
        {
            OnInformation("Configuring {0}", pageModel.GetType().Name);

            var cfgDef = pageModel.GetType().GetInterfaces().FirstOrDefault(w => w.IsGenericType() && w.GetGenericTypeDefinition() == typeof(IViewModelWithCfg<>));
            if (cfgDef == null) return;

            var cfgType = vmCfg == null ? cfgDef.GenericTypeArguments[0] : vmCfg.GetType();
            var vmTypeNeeded = typeof(IViewModelWithCfg<>).MakeGenericType(cfgType);

            //was the cfg provided of the correct type?
            if (pageModel.ImplementsInterface(vmTypeNeeded))
                pageModel.Invoke("Configure", vmCfg ?? Activator.CreateInstance(cfgDef.GenericTypeArguments[0]));
        }

        private void ConnectToReactors(object reaction, IManageResources resourceManager)
        {
            if (reaction.ImplementsInterface(typeof(IRxnPublisher<IRxn>)))
            {
                OnVerbose("Connecting Publisher", reaction.GetType());

                var page = reaction as IRxnPublisher<IRxn>;
                var pageReactor = RxnCreator.GetReactorFor(page, name => _reactorManager.StartReactor(name).Reactor);

                pageReactor.Connect(page).DisposedBy(resourceManager);
            }

            if (reaction.ImplementsInterface(typeof(IReactTo<IRxn>)))
            {
                OnVerbose("Connecting ReactTo", reaction.GetType());

                var page = reaction as IReactTo<IRxn>;
                var pageReactor = RxnCreator.GetReactorFor(page, name => _reactorManager.StartReactor(name).Reactor);

                pageReactor.Connect(page, RxnSchedulers.TaskPool).DisposedBy(resourceManager);
            }
        }
        
        private static string GetPageTypeName(Type pageModelType)
        {
            return pageModelType.AssemblyQualifiedName
                .Replace("PageModel", "Page")
                .Replace("ViewModel", "Page");
        }
    }
}



