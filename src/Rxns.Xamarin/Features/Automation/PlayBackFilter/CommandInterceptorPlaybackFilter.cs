using Rxns.Interfaces;
using Rxns.Xamarin.Features.UserDomain;

namespace Rxns.Xamarin.Features.Automation.PlayBackFilter
{
    public class CommandInterceptorPlaybackFilter : ITapePlaybackFilter
    {
        public IRxn FilterPlayback(IRxn e)
        {
            //only executed message should be played back
            return e as UserExecuted;
        }
    }
}
