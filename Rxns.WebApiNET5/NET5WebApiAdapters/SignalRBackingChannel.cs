using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class SignalRBackingChannel<T> //: IEventBackingChannel<T>, IAmReactive
    {
        private readonly IEventHubClient _hubClient;
        public IScheduler DefaultScheduler { get; set; }

        public SignalRBackingChannel(IEventHubClient hubClient)
        {
            _hubClient = hubClient;
        }

        private Subject<T> _channel;   

        public IObservable<T> Setup()
        {
            _channel = new Subject<T>();

            _hubClient.Connect().Subscribe(o =>
            {
                
            });

            _hubClient.CreateSubscription();

            return _channel.ObserveOn(DefaultScheduler).SubscribeOn(DefaultScheduler);
        }

        public void Publish(T message)
        {
            throw new NotImplementedException();
        }
    }
}
