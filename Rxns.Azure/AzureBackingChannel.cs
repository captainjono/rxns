using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using Azure.Storage.Queues;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Azure
{
    public interface IAzureConfiguration
    {
        string StorageConnectionString { get; }

    }

    public class AzureCfg : IAzureConfiguration
    {
        public string StorageConnectionString { get; set; }
    }


    /// <summary>
    /// This backing channel uses windows azure as its storage mechanism.
    /// Use the azure backing channel to create distributed messaging infrastructure that
    /// will reliably send messages across platforms by specififying identical storage accounts.
    /// 
    /// Azure charges for each message that is received and sent over the queue pipeline, the backing store
    /// implements a back-off function by default to reduce costs. 
    /// 
    /// todo: implement functionality to expose more azure queue settings for better customisation
    /// todo: implement mechansim to allow custom backoff procedures to be run
    /// </summary>
    public class AzureBackingChannel<T> : ReportsStatus, IRxnBackingChannel<T>
    {
        private readonly string _queueName;
        private QueueClient _messageQueuePub;
        private readonly IAzureConfiguration _azureCfg;
        private IResolveTypes _typeResolver;

        /// <summary>
        /// Indicates if the backing channel should never poll the queue for new messages,
        /// only publish messages to it
        /// </summary>
        public bool PublishOnly { get; set; }

        /// <summary>
        /// creates a new instance. the queues are not activated until setup() is called
        /// </summary>
        /// <param name="azureCfg">We leverage the StorageConnectionString from this interface</param>
        /// <param name="queueName">The name of the queue to use as the channel</param>
        /// <param name="publishOnlyMode">This indicates that the channel will only publish messages to the queue, never poll it for new messages. ie. setup() will never produce a value</param>
        /// <param name="serialiser">Used for serilisating messages to the queue</param>
        public AzureBackingChannel(IAzureConfiguration azureCfg, IResolveTypes typeResolver, string queueName = "eventsink", bool publishOnlyMode = false)
        {
            PublishOnly = publishOnlyMode;
            _reporterName = "AzureBackingChannel<{0}>".FormatWith(queueName);
            _queueName = queueName;
            _azureCfg = azureCfg;
            _typeResolver = typeResolver;
        }

        public void Publish(T message)
        {
            _messageQueuePub.SendMessage(message.Serialise().ResolveAs(message.GetType()));
        }

        public IObservable<T> Setup(IDeliveryScheme<T> postman)
        {
            try
            {
                OnVerbose("Starting");

                if (_messageQueuePub == null)
                {
                    _messageQueuePub = new QueueClient(_azureCfg.StorageConnectionString, _queueName, new QueueClientOptions() { MessageEncoding = QueueMessageEncoding.None });
                    if (!_messageQueuePub.Exists())
                    {
                        _messageQueuePub.Create();
                    }
                }

                if (!PublishOnly)
                {
                    return Rxn.Create<T>(o =>
                    {
                        var idle = 0;

                        return Rxn.On(NewThreadScheduler.Default, () =>
                        {
                            Thread.CurrentThread.Name = "AzureQueueWorker";
                            OnInformation("Listening to azure queue");

                            return Rxn.DfrCreate(() => _messageQueuePub.ReceiveMessageAsync().ToObservable())
                                .SelectMany(msg =>
                                {
                                    try
                                    {
                                        if (msg.Value == null)
                                            return HandleQueueEmpty(idle++).Then();

                                        idle = 0;

                                        //hmm need todo something about reliable delivery? or is that for the eventhub?
                                        //var confirmationManager = new AutoConfirmingManager<string>();
                                        //confirmationManager.Information.Subscribe(ReportInformation).DisposedBy(this);
                                        //confirmationManager.Errors.Subscribe(ReportExceptions).DisposedBy(this);
                                        _messageQueuePub.DeleteMessage(msg.Value.MessageId, msg.Value.PopReceipt);
                                        //----

                                        var msgAsJson = Encoding.UTF8.GetString(msg.Value.Body);
                                        var msgType = msgAsJson.GetTypeFromJson(_typeResolver);
                                        o.OnNext((T)msgAsJson.Deserialise(msgType));
                                    }
                                    catch (Exception e)
                                    {
                                        OnError(e);
                                    }

                                    return TimeSpan.Zero.Then();
                                })
                                .Repeat()
                                .Until(o.OnError);
                        }).Until(o.OnError);
                    });
                }

                return Observable.Empty<T>();
            }
            catch (Exception e)
            {
                OnError(e);
                throw;
            }
        }

        /// <summary>
        /// Implements the backoff procedure that is used to reduce azure costs by not polling
        /// the queue in times of low load
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="idleCount">The value indicating how many times the queue listener has been idle.</param>
        /// <param name="delay">The time interval during which the queue listener will be instructed to sleep before performing next unit of work.</param>
        /// <returns>A boolean flag indicating that the queue listener should stop processing any further work and must terminate.</returns>
        private static TimeSpan HandleQueueEmpty(int idleCount)
        {
            // Set up the initial parameters, read configuration settings.
            int deltaBackoffMs = 200;

            // int minimumIdleIntervalMs = Convert.ToInt32(config.Settings.MinimumIdleInterval.TotalMilliseconds);
            // int maximumIdleIntervalMs = Convert.ToInt32(config.Settings.MaximumIdleInterval.TotalMilliseconds);
            int minimumIdleIntervalMs = 200;
            int maximumIdleIntervalMs = 60000; //3600000; // 1hour

            // Calculate a new sleep interval value that will follow a random exponential back-off curve.
            int delta = (int)((Math.Pow(2.0, (double)idleCount) - 1.0) * (new Random()).Next((int)(deltaBackoffMs * 0.8), (int)(deltaBackoffMs * 1.2)));
            int interval = Math.Min(minimumIdleIntervalMs + delta, maximumIdleIntervalMs);

            // Pass the calculated interval to the dequeue task to enable it to enter into a sleep state for the specified duration.
            return TimeSpan.FromMilliseconds((double)interval < 1 ? 1 : interval);
        }
    }


}


