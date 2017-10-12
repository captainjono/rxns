using Rxns.Interfaces;
using Rxns.Xamarin.Features.UserDomain;

namespace Rxns.Xamarin.Features.Automation.PlayBackFilter
{
    public class UserActionsOnlyFilter : ITapePlaybackFilter
    {
        public IRxn FilterPlayback(IRxn tapedEvent)
        {
            return tapedEvent as IUserAction;
        }
    }
}
