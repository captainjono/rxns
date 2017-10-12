using System;
using System.Reactive.Subjects;
using Rxns.Interfaces;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    public class RxnViewModel : RxnPageModel, IReactTo<IRxn>
    {
        protected Action<IRxn> _publish;
        protected RxnSource me = RxnSource.Create();

        public ISubject<IRxn> Input { get; private set; }
        public ISubject<IRxn> Output { get; private set; }

        public RxnViewModel(INavigationService<IRxnPageModel> nav = null)
            : base(nav)
        {
            Input = new Subject<IRxn>();
            Output = new Subject<IRxn>();

            _publish = e => Output.OnNext(e);
        }
    }
}
