using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Xamarin.Features.Composition;
using Rxns.Xamarin.Features.Navigation.Pages;
using Rxns.Xamarin.Features.UserDomain;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public class PhoneNavigationOrchestrator : ReportsStatus, INavigationOrchestrator, IRxnCfg, IRxnPublisher<IRxn>
    {
        public class PoppedPage : IUserAction { }

        private readonly IAppNav<Page, IRxnPageModel> _appNav;
        private readonly IAppPages _defaultPages;
        private readonly IResolvePages _resolver;
        private readonly Action<Page> _setMainPage;
        private HomeNavigationPage _mainNavigationPage;
        private Action<IRxn> _publish;

        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline.BufferFirstLastDistinct(e => e.GetType().Name, //only buffer the same events from happening in quick succession "a user quickly tappning a key"
                                                    TimeSpan.FromMilliseconds(400), true, false);
        }

        public string Reactor { get; private set; }
        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; private set; }
        public bool MonitorHealth { get { return false; } }

        public PhoneNavigationOrchestrator(IAppNav<Page, IRxnPageModel> appNav, IAppPages defaultPages, IResolvePages resolver, Action<Page> setMainPage)
        {
            _appNav = appNav;
            _defaultPages = defaultPages;
            _resolver = resolver;
            _setMainPage = setMainPage;
        }

        private void DisposeCurrentPageWhenBackNavigationPressed(object sender, NavigationEventArgs e)
        {
            DisposeOfPageResources(e.Page);
            Pop();
            _publish(new PoppedPage());
        }

        private void Push(Page page, IRxnPageModel model)
        {
            if (_appNav.Current != null) _appNav.Current.Model.Sleep();

            _appNav.Push(page, model);
            VerboseLogPageInfo();
        }

        private void Swap(Page page, IRxnPageModel model)
        {
            if (_appNav.Current != null) _appNav.Current.Model.Hide();

            _appNav.PushRoot(page, model);
            VerboseLogPageInfo();
        }

        private void Pop()
        {
            _appNav.Pop();
            if (_appNav.Current == null) return;

            _appNav.Current.Model.Wake();
            VerboseLogPageInfo();
        }

        /// <summary>
        /// Pops the appNav to the specified pageModel (or the root page) and 
        /// then returns the  number of pops made;
        /// </summary>
        /// <param name="pageModel"></param>
        /// <returns></returns>
        private int PopTo(Type pageModel)
        {
            var pagesToPop = 0;
            while (_appNav.Current.Model != pageModel && _appNav.Previous.Any())
            {
                pagesToPop++;
                PopQuietly();
            }
            _appNav.Current.Model.Wake();
            VerboseLogPageInfo();
            return pagesToPop;
        }
        /// <summary>
        /// Pops from the nav stack without waking the model
        /// </summary>
        private void PopQuietly()
        {
            _appNav.Pop();
        }

        private void VerboseLogPageInfo()
        {
            OnVerbose("NavStack({2}){1} ==> {0}", _appNav.Current != null ? _appNav.Current.ToString() : "nothing", _appNav.Previous.Count != 0 ? _appNav.Previous.Peek().ToString() : "nothing", _appNav.Previous.Count);
        }

        /// <summary>
        /// page life-cycle -> Currently the system doesnt dispose/hide the previous page until its "hidden" or popped
        /// </summary>
        /// <param name="navTo"></param>
        /// <returns></returns>
        public IObservable<IRxn> Process(NavigationAction navTo)
        {
            return Rxn.Create<IRxn>(o =>
            {
                if (navTo.IsPushing || navTo.IsSwapping)
                {
                    OnVerbose("Resolving next page: {0}", navTo.PageModelType.Name);

                    var page = ResolvePageFor(navTo.PageType, navTo.PageModelType, navTo.Cfg);
                    var nextPageModel = GetPageModel(page);

                    if (!navTo.IsSwapping)
                    {
                        Push(page, nextPageModel);
                        return navTo.IsModal
                            ? ShowPageModal(page).FinallyR(() => o.OnCompleted()).Subscribe()
                            : ShowPage(page).FinallyR(() => o.OnCompleted()).Subscribe();
                    }
                    else
                    {
                        return ShowRootPage(page, navTo.ShowNav).FinallyR(() => o.OnCompleted()).Subscribe();
                    }
                }
                else if (navTo.IsPoppingTo)
                {
                    OnVerbose("Disposing from: {0} to {1}", _appNav.Current, navTo.PageModelType);
                    var pagesToPop = PopTo(navTo.PageModelType);

                    return navTo.IsModal ?
                        HideManyPagesModal(pagesToPop).FinallyR(() => o.OnCompleted()).Subscribe() :
                        HideManyPages(pagesToPop).FinallyR(() => o.OnCompleted()).Subscribe();
                }
                else //popping
                {
                    OnVerbose("Disposing: {0}", _appNav.Current);

                    return navTo.IsModal ?
                       HideCurrentPageModal().FinallyR(() => o.OnCompleted()).Subscribe() :
                       HideCurrentPage().FinallyR(() => o.OnCompleted()).Subscribe();

                    //pages are implicitly pop()'d  when OnPopped event is called as it is removed from the nav stack
                    /*return (navTo.IsModal ? HideCurrentPageModal() : HideCurrentPage())
                                          .DoWhile(() => navTo.PageModelType != null && _appNav.Current.Model.GetType() != navTo.PageModelType)
                                          .FinallyR(() => o.OnCompleted())
                                          .Subscribe();*/
                }
            })
            .Finally(() => OnVerbose("Finished navigation"))
            .Catch<IRxn, Exception>(e =>
            {
                OnError(e);
                return Observable.Empty<IRxn>();
            });
        }



        private Page ResolvePageFor(Type page, Type model, object cfg)
        {
            return page == null ?
                _resolver.ResolvePageWithModel(model, cfg) : //infer the type of page
                _resolver.ResolvePageWithModel(page, _resolver.ResolvePageModel(model, cfg), cfg, true, false); //build page up explicitly
        }


        private void PushRoot(Page page, IRxnPageModel model, bool hasNavBar = false)
        {
            var oldMain = _mainNavigationPage;
            if (_mainNavigationPage != null)
            {
                Pop();
            }

            _mainNavigationPage = new HomeNavigationPage(page, hasNavBar);
            _mainNavigationPage.Popped += DisposeCurrentPageWhenBackNavigationPressed;
            new DisposableAction(() => _mainNavigationPage.Popped -= DisposeCurrentPageWhenBackNavigationPressed).DisposedBy(this);
            Swap(page, model);

            RxnAppCfg.UIScheduler.Run(() =>
            {
                _setMainPage(page);
                //only dispose of the loginpage once we are loaded
                //top stop the going black

                RxnAppCfg.BackgroundScheduler.Run(() =>
                {
                    if (oldMain != null)
                    {
                        DisposeOfPageResources(oldMain);
                    }
                });
            });
        }

        public IObservable<IRxn> Process(UserLoggedIn @event)
        {
            return Rxn.Create(() =>
            {
                var mainPage = _defaultPages.MainPage();
                PushRoot(mainPage, (IRxnPageModel)mainPage.BindingContext, _defaultPages.MainPageHasNav);
            })
            .Select(_ => (IRxn)null);
        }

        public IObservable<IRxn> Process(UserLoggingOut @event)
        {
            return Rxn.Create(() =>
            {
                var loginPage = _defaultPages.LoginPage();
                PushRoot(loginPage, (IRxnPageModel)loginPage.BindingContext);
            })
            .Select(_ => (IRxn)null);
        }

        private static IRxnPageModel GetPageModel(Page page)
        {
            if (page != null && page.BindingContext is IRxnPageModel)
            {
                return page.BindingContext as IRxnPageModel;
            }
            return null;
        }

        public IObservable<Unit> ShowRootPage(Page page, bool hasNavbar = false)
        {
            return Rxn.Create(() =>
            {
                OnInformation("Swapping>> {0}", page.GetType());
                PushRoot(page, (IRxnPageModel)page.BindingContext, hasNavbar);
            });
        }

        public IObservable<Unit> ShowPage(Page page)
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                OnInformation("Showing>> {0}", page.GetType());
                _mainNavigationPage.Navigation.PushAsync(page, true);
            });
        }

        public IObservable<Unit> ShowPageModal(Page page)
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                OnInformation("ShowingModal>> {0}", page.GetType());
                _mainNavigationPage.Navigation.PushModalAsync(page, true);
            });
        }

        public IObservable<Unit> HideCurrentPage()
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                _mainNavigationPage.Navigation.PopAsync(true);//.Wait();
            });
        }

        public IObservable<Unit> HideCurrentPageModal()
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                _mainNavigationPage.Navigation.PopModalAsync(true); //.Wait();
            });
        }
        //
        public IObservable<Unit> HideManyPages(int pagesToPop)
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                var i = 0;
                do
                {
                    _mainNavigationPage.Navigation.PopAsync(true);
                    i++;
                }
                while (i < Math.Abs(pagesToPop));
            });
        }

        public IObservable<Unit> HideManyPagesModal(int pagesToPop)
        {
            return RxnAppCfg.UIScheduler.Run(() =>
            {
                var i = 0;
                do
                {
                    _mainNavigationPage.Navigation.PopModalAsync(true);
                    i++;
                }
                while (i < Math.Abs(pagesToPop));
            });
        }

        private void DisposeOfPageResources(Page page)
        {
            this.TryCatch(() =>
            {
                OnVerbose("Disposing of {0} resources", page.GetType());

                if (page.BindingContext is IRxnPageModel)
                {
                    OnVerbose("Hiding {0}", page.BindingContext.GetType());
                    ((IRxnPageModel)page.BindingContext).Hide();
                    ((IRxnPageModel)page.BindingContext).Dispose();
                }

                if (page is IDisposable)
                {
                    OnVerbose("Disposing of {0}", page.GetType());
                    ((IDisposable)page).Dispose();
                }

                var tabbedPage = page as TabbedPage;
                if (tabbedPage != null)
                    tabbedPage.Children.ForEach(o => DisposeOfPageResources(o));

            });
        }

        public IObservable<IRxn> Process(EventPublishingOsBridge.AppResumed @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                if (_appNav.Current != null && _appNav.Current.Model != null)
                {
                    OnInformation("Resuming app @ {0}", _appNav.Current);
                    RxnAppCfg.BackgroundScheduler.Run(() => this.TryCatch(() => _appNav.Current.Model.BackgroundShow()));
                }
                else
                    OnWarning("No current page set, cannot resume page, this is probably means an error has occoured!");
            });
        }

        public IObservable<IRxn> Process(EventPublishingOsBridge.AppBackgrounded @event)
        {
            OnVerbose("App going to background, doing nothing");
            return Observable.Empty<IRxn>();
        }

        public void ConfigiurePublishFunc(Action<IRxn> eventFunc)
        {
            _publish = eventFunc;
        }
    }
}

