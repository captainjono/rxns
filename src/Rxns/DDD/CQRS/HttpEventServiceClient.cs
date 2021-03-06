﻿using System;
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
        public TimeSpan? Duration { get; set; } = TimeSpan.FromMinutes(5);

        public StreamLogs()
        {
        }

        public StreamLogs(string durationInMins)
        {
            try
            {
                Duration = TimeSpan.FromMinutes(Int32.Parse(durationInMins));
            }
            catch (Exception e)
            {
                
            }

            try
            {
                Duration = TimeSpan.Parse(durationInMins);
            }
            catch (Exception e)
            {

            }
        }
    }

    public class HttpEventsServiceClient : AuthenticatedServiceClient, IServiceCommandHandler<StreamLogs>
    {
        private readonly IAppServiceRegistry _cfg;
        private readonly IAppContainer _app;
        private readonly IRxnAppInfo _appInfo;
        private IDisposable _stopStream;
        private readonly StringBuilder _eventsAsJsonMemPool = new StringBuilder();


        public HttpEventsServiceClient(IAppServiceRegistry cfg, IHttpConnection connection, IAppContainer app, IRxnAppInfo appInfo)
            : base(connection)
        {
            _cfg = cfg;
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

            _stopStream?.Dispose();
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
                    return c.PostAsync(WithBaseUrl("events/publish"), new StringContent(e));
                }).Select(_ => new Unit()).Subscribe(o);
            });
        }

        protected override string BaseUrl()
        {
            return _cfg.AppStatusUrl;
        }
    }
}
