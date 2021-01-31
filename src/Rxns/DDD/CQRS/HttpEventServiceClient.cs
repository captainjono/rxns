using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.DDD.CQRS
{
    public class StreamLogs : ServiceCommand
    {
        public TimeSpan? Duration { get; set; }

        public StreamLogs(TimeSpan? duration = null)
        {
            Duration = duration;
        }
    }

    public class HttpEventsServiceClient : AppServicesClient, IRxnProcessor<StreamLogs>
    {
        private readonly IAppContainer _app;
        private readonly IRxnAppInfo _appInfo;
        private IDisposable _stopStream;

        public HttpEventsServiceClient(CommandServiceClientCfg cfg, IHttpConnection connection, IAppContainer app, IRxnAppInfo appInfo)
            : base(cfg, connection)
        {
            _app = app;
            _appInfo = appInfo;
        }

        public IObservable<IRxn> Process(StreamLogs command)
        {
            StartOrStopStreaming(command.Duration ?? TimeSpan.FromMinutes(10));

            return CommandResult.Success().ToObservable();
        }

        private void StartOrStopStreaming(TimeSpan forHowLong)
        {
            if (_stopStream != null)
            {
                _stopStream.Dispose();
                _stopStream = null;
            }
            else
            {  
                _stopStream = _app.Information.Select(i => i.ToRxn(_appInfo.Name))
                    .Merge(_app.Errors.Select(e => e.ToRxn(_appInfo.Name)))
                    .Buffer(TimeSpan.FromSeconds(5))
                    .Where(e => e.AnyItems())
                    .SelectMany(Publish)
                    .Timeout(forHowLong)
                    .Catch<Unit, Exception>(_ => Rxn.Empty<Unit>())
                    .Until(OnError);
            }
        }

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create<Unit>(o =>
            {
                var eventsAsJson = new StringBuilder();
                events.ForEach(e => eventsAsJson.AppendFormat("{0}\r\n\r", e.GetPropertyDef("T") != null ? e.Serialise() : e.Serialise().ResolveAs(e.GetType())));

                return Connection.Call(c => c.PostAsync(WithBaseUrl("publish"), new StringContent(eventsAsJson.ToString()))).Select(_ => new Unit()).Subscribe(o);
            });
        }

        protected override void SetConfiguration(CommandServiceClientCfg cfg)
        {
            BaseUrl = cfg.BaseUrl;
        }
    }
}
