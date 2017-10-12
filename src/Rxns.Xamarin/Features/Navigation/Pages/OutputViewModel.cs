using System;
using Rxns.Interfaces;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    public class OutputViewModel : RxnPageModel, IRxnPublisher<IRxn>
    {
        protected Action<IRxn> _publish;

        public OutputViewModel(INavigationService<IRxnPageModel> nav = null)
            : base(nav)
        {
        }

        public void ConfigiurePublishFunc(Action<IRxn> eventFunc)
        {
            _publish = eventFunc;
        }
    }
}
