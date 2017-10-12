using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// An in-process backing channel leveraging a RxSubject
    /// </summary>
    /// <typeparam name="T">The type of data the channel will transport</typeparam>
    public class LocalBackingChannel<T> : IRxnBackingChannel<T>
    {
        public IScheduler DefaultScheduler { get; set; }
        protected IDeliveryScheme<T> _postman; 

        /// <summary>
        /// We want this channel to be async by default, using the taskpool
        /// to schedule observation logic. This will stop any errors from breaking
        /// the channel
        /// </summary>
        public LocalBackingChannel(IScheduler defaultScheduler = null)
        {
            DefaultScheduler = defaultScheduler ?? TaskPoolScheduler.Default;
        }

        protected Subject<T> BackingChannel;    

        public virtual IObservable<T> Setup(IDeliveryScheme<T> postman)
        {
            _postman = postman;
            if (BackingChannel != null)
            {
                //incase an error occoured, dont send completed
                if(BackingChannel.HasObservers)
                    BackingChannel.OnCompleted();

                BackingChannel.Dispose();
            }

            BackingChannel = new Subject<T>();

            return BackingChannel.ObserveOn(DefaultScheduler).SubscribeOn(DefaultScheduler);
        }

        public virtual void Publish(T message)
        {
            _postman.Deliver(message, e => BackingChannel.OnNext(e));
        }
    }
}
