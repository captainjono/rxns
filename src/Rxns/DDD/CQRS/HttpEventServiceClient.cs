using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.DDD.CQRS
{
    public class StreamLogs : ServiceCommand
    {
        public TimeSpan? Duration { get; set; }

        public StreamLogs() : this(TimeSpan.FromMinutes(5))
        {
        }

        public StreamLogs(string durationInMins) : this(TimeSpan.FromMinutes(Int32.Parse(durationInMins)))
        {
        }

        public StreamLogs(TimeSpan duration)
        {
            Duration = duration;
        }
    }

    public class HttpEventsServiceClient : AppServicesClient, IServiceCommandHandler<StreamLogs>
    {
        private readonly IAppContainer _app;
        private readonly IRxnAppInfo _appInfo;
        private IDisposable _stopStream;
        private readonly StringBuilder _eventsAsJsonMemPool = new StringBuilder();


        public HttpEventsServiceClient(CommandServiceClientCfg cfg, IHttpConnection connection, IAppContainer app, IRxnAppInfo appInfo)
            : base(cfg, connection)
        {
            _app = app;
            _appInfo = appInfo;
        }

        public IObservable<CommandResult> Handle(StreamLogs command)
        {
            return Rxn.Create(() =>
            {
                StartOrStopStreaming(command.Duration ?? TimeSpan.FromMinutes(10));
                
                return CommandResult.Success();
            });
        }

        private void StartOrStopStreaming(TimeSpan forHowLong)
        {
            if (_stopStream != null)
            {
                StopStreaming();
            }
            else
            {  
                OnInformation($"Log streaming started for {forHowLong}");
                _stopStream = Rxn.MakeReliable(() => _app.Information.Select(i => i.ToRxn(_appInfo.Name))
                    .Merge(_app.Errors.Select(e => e.ToRxn(_appInfo.Name)))
                    .Merge(Logger.OnDebug.Select(i => new RLM() { L = i.ToString(), S = _appInfo.Name }))
                    //.Merge(ReportStatus.Log.Errors.Select(e => e.ToRxn(_appInfo.Name)))
                    .Buffer(TimeSpan.FromSeconds(5))
                    .Where(e => e.AnyItems())
                    .SelectMany(Publish),
                    e => OnError("StreamLogs", e)).Until(OnError);

                forHowLong.Then().Do(_ => StopStreaming()).Until(OnError);
            }
        }

        private void StopStreaming()
        {
            OnInformation($"Log streaming stopped");

            _stopStream.Dispose();
            _stopStream = null;
        }

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create<Unit>(o =>
            {
                //todo: convert to using RxnStream api
                events.ForEach(e => _eventsAsJsonMemPool.AppendFormat("{0}\r\n\r", e.GetPropertyDef("T") != null ? e.Serialise() : e.Serialise().ResolveAs(e.GetType())));

                return Connection.Call(c =>
                {
                    //should put a lock here so we dont miss any
                    var e = _eventsAsJsonMemPool.ToString();
                    _eventsAsJsonMemPool.Clear();;
                    return c.PostAsync(WithBaseUrl("publish"), new StringContent(e));
                }).Select(_ => new Unit()).Subscribe(o);
            });
        }

        protected override void SetConfiguration(CommandServiceClientCfg cfg)
        {
            BaseUrl = cfg.BaseUrl;
        }
    }
}
