using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    public interface IRxnPageModel : IManageResources
    {
        void Create();
        void Sleep();
        void Wake();
        void BackgroundShow();
        IDisposable Show();
        IObservable<IDisposable> ShowLongRunning();
        void Hide();
    }
    
    public class RxnPageModel : ReportsStatus, IViewModel, INotifyPropertyChanged, IRxnPageModel
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string PageTitle { get; protected set; }
        public string Icon { get; protected set; }

        protected readonly INavigationService<IRxnPageModel> _nav;

        private bool _shownViewOnce = false;
        private readonly List<IDisposable> _onHide = new List<IDisposable>();

        public bool IsLoading { get; protected set; }

        public RxnPageModel(INavigationService<IRxnPageModel> nav = null)
        {
            _nav = nav;
        }

        public virtual void Create()
        {
            ShowView();
        }

        public virtual void Sleep()
        {
            
        }

        public virtual void Wake()
        {
            
        }

        public virtual void BackgroundShow()
        {

        }

        private void ShowView()
        {
            if(_shownViewOnce) return;
            IsLoading = true;
            _shownViewOnce = true;

            Show().DisposedBy(_onHide);
            RxnAppCfg.BackgroundScheduler.Run(() => this.TryCatch(() => ShowLongRunning().Do(resources => resources.DisposedBy(_onHide))
                                                                                       .FinallyR(() => IsLoading = false)
                                                                                       .Until(OnError)
                                                                                       .DisposedBy(_onHide))
                                            ).Subscribe();
        }

        public virtual IDisposable Show()
        {
            return Disposable.Empty;
        }

        public virtual IObservable<IDisposable> ShowLongRunning()
        {
            return Disposable.Empty.ToObservable();
        }
        
        public virtual void Hide()
        {
            OnVerbose("Hiding");

            _onHide.DisposeAll();
            _onHide.Clear();
        }
    }
}

