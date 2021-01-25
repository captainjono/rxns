using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns
{
    /// <summary>
    /// An rxn manager facilitates for reliable delivery rxn messages ion a publish/subscribe pattern
    /// 
    /// Any T that can be serialised can be an rxn message, and filtering can be achieved
    /// when subscribing to the rxn sink.
    /// </summary>
    public class RxnManager<T> : ReportsStatus, IRxnManager<T>
    {
        private readonly IDeliveryScheme<T> _postman;
        public IScheduler DefaultScheduler { get; set; }

        private readonly Subject<object> _inChannel = new Subject<object>();
        private readonly IRxnBackingChannel<T> _channel;
        private IDisposable _queueSub;
        private bool _isActive = false;

        public override string ReporterName
        {
            get { return "Event Manager"; }
        }

        /// <summary>
        /// creates a new instance of the rxn manager
        /// </summary>
        /// <param name="channel">the channel that will back the rxn sink</param>
        /// <param name="postman"></param>
        /// <param name="subscription"></param>
        public RxnManager(IRxnBackingChannel<T> channel, IDeliveryScheme<T> postman = null, IScheduler subscription = null)
        {
            DefaultScheduler = subscription ?? TaskPoolScheduler.Default;
            _postman = postman ?? new NoGaretenee<T>();
            _channel = channel;

            Errors.Subscribe(this, error =>
            {
                OnWarning("Backing channel threw an error: {0}", error.Message);
                OnVerbose("Event Manager is restarting due to error");
                //at the moment, if an error is detected lets try and reactivate the queue
                //because its probably died for some reason.
                _isActive = false;
                Activate().Until(OnError);
            });
        }

        public IObservable<Unit> Activate()
        {
            return Rxn.Create<Unit>(o =>
            {
                if (!_isActive)
                {
                    OnInformation("Activating rxn manager with backing channel: {0}", _channel.GetType().Name);

                    if (_queueSub != null)
                        _queueSub.Dispose();

                    _queueSub = _channel.Setup(_postman).Subscribe(this, message =>
                        {
                            if(typeof(RLM) != message.GetType())
                                OnVerbose("Message received: {0}", message.GetType().Name);

                            _inChannel.OnNext(message);
                        });
                }
                _isActive = true;

                o.OnNext(new Unit());
                o.OnCompleted();
                return Disposable.Empty;
            });
        }

        /// <summary>
        /// creates a subscription to all events in the rxn sink
        /// </summary>
        /// <returns>the subscription</returns>
        public IObservable<T> CreateSubscription()
        {
            return CreateSubscription<T>();
        }

        /// <summary>
        /// creates a new subscription to the rxn sink
        /// </summary>
        /// <typeparam name="TMessageType">The type of messages this subscription will receive. uses type inheritance to filter messages</typeparam>
        /// <returns></returns>
        public IObservable<TMessageType> CreateSubscription<TMessageType>()
        {
            OnVerbose("Creating subscription for '{0}' of type: {1}", PlatformHelper.CallingTypeName, typeof(TMessageType).Name);
            return Activate().SelectMany(_ => _inChannel.OfType<TMessageType>().ObserveOn(DefaultScheduler));
        }

        private static readonly object singleThread = new object();
        /// <summary>
        /// publishes a message to all subscribers
        /// </summary>
        /// <param name="message">The message to send</param>
        public IObservable<Unit> Publish(T message)
        {
            return Rxn.Create<Unit>(o =>
            {
                lock(singleThread)
                {
                    if (!_isActive)
                        return Activate().Do(_ => PublishMsg(message)).Subscribe();
                    else
                        PublishMsg(message);
                    
                    o.OnNext(new Unit());
                    o.OnCompleted();
                    return Disposable.Empty;
                }
            });
        }

        private void PublishMsg(T message)
        {
            if (message == null)
                OnWarning("Cannot publish null value");
            else
            {
                //  OnVerbose("Message published: {0}", message.GetType().Name);
                _channel.Publish(message);
            }
        }
        
        public override void Dispose()
        {
            if (!IsDisposed)
            {
                _inChannel.OnCompleted();
                _inChannel.Dispose();

                base.Dispose();
            }
        }
    }

    public static class RxnManagerExt
    {
        public static IObservable<T> Ask<T>(this IRxnManager<IRxn> rxnManager, IUniqueRxn question) where T : IRxnResult, IRxn
        {
            return Rxn.DfrCreate<T>(o =>
            {
                var resources = new CompositeDisposable(2);

                rxnManager.CreateSubscription<T>()
                    .Where(c => c.InResponseTo == question.Id)
                    .FirstOrDefaultAsync()
                    .Subscribe(o)
                    .DisposedBy(resources);

                rxnManager.Publish(question).Until(o.OnError).DisposedBy(resources);

                return resources;
            })
                .Finally(() => ReportStatus.Log.OnVerbose($"Got answer to question {question.Id}"));
        }
    }

}
