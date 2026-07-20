using System;
using Rxns.Playback;

namespace Rxns.Xamarin.Features.Automation
{
    public interface ITapeRepository
    {
        void Delete(string name);
        ITapeStuff GetOrCreate(string name);
        IObservable<ITapeStuff[]> GetAll();
    }
}
