using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public class RxnAppNavigator : IRxAppNav<Page, IRxnPageModel>
    {
        private readonly ReplaySubject<AppPageInfo<Page, IRxnPageModel>> _view;
        public AppPageInfo<Page, IRxnPageModel> Current { get; set; }
        public Stack<AppPageInfo<Page, IRxnPageModel>> Previous { get; set; }

        public RxnAppNavigator()
        {
            Previous = new Stack<AppPageInfo<Page, IRxnPageModel>>();
            _view = new ReplaySubject<AppPageInfo<Page, IRxnPageModel>>(1);
        }

        public void Push(Page nextPage, IRxnPageModel nextModel)
        {
            if (Current != null) Previous.Push(Current);

            Current = new AppPageInfo<Page, IRxnPageModel>(nextPage, nextModel);
            CurrentThreadScheduler.Instance.Run(() =>_view.OnNext(Current));
        }

        public void Pop()
        {
            if(Previous.Count == 0) return;

            Current = Previous.Pop();
            CurrentThreadScheduler.Instance.Run(() => _view.OnNext(Current));
        }

        public void PushRoot(Page page, IRxnPageModel nextModel)
        {
            Previous.Clear();

            Current = new AppPageInfo<Page, IRxnPageModel>(page, nextModel);
            CurrentThreadScheduler.Instance.Run(() => _view.OnNext(Current));
        }

        public IObservable<AppPageInfo<Page, IRxnPageModel>> CurrentView { get { return _view; } }
    }
}
