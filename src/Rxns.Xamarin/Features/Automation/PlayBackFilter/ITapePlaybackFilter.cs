using Rxns.Interfaces;

namespace Rxns.Xamarin.Features.Automation.PlayBackFilter
{
    public interface ITapePlaybackFilter
    {
        IRxn FilterPlayback(IRxn tapedEvent);
    }
}
