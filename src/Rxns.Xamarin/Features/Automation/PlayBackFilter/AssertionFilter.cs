using System;
using System.Reactive.Linq;
using Rxns.Interfaces;
using Rxns.Xamarin.Features.Navigation;
using Rxns.Xamarin.Features.Navigation.Pages;
using Rxns.Xamarin.Features.UserDomain;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Automation.PlayBackFilter
{
    public class AssertionFilter : ITapePlaybackFilter
    {
        private readonly IAppNav<Page, IRxnPageModel> _appNav;

        public class AssertModel : IRxn
        {
            public string Value { get; private set; }

            public AssertModel() { }
            public AssertModel(string value)
            {
                Value = value;
            }
        }

        public AssertionFilter(IAppNav<Page, IRxnPageModel> appNav, IRxnManager<IRxn> eventManager)
        {
            _appNav = appNav;
            eventManager.CreateSubscription<ShakeGesture>().Do(_ => eventManager.Publish(new AssertModel(Snapshot(_appNav.Current.Model)))).Until();
        }

        public IRxn FilterPlayback(IRxn tapedEvent)
        {
            try
            {
                var assert = tapedEvent as AssertModel;
                if (assert != null)
                {
                    var currentValue = Snapshot(_appNav.Current.Model);
                    //need a better comparision, like the cmdServiceCache hashing function to be ok with datetimes etc that will always be different
                    Ensure.Equal(currentValue.ToJson(), assert.Value.ToJson(), "The model should be the same as when originally recorded");
                    return null;
                }

                return tapedEvent;
            }
            catch (Exception e)
            {
                return new ToastAlert("Play inconsitancy detected", e.Message, LogLevel.Fatal);
            }
        }

        private string Snapshot(IRxnPageModel model)
        {
            return model.ToJson();
        }
    }
}
