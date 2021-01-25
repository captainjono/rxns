using System;
using System.Diagnostics;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;

namespace Rxns.Health
{
    public class AppOpTimer : HealthEvent
    {
        public string Name { get; set; }
        public TimeSpan Speed { get; set; }
        public TimeSpan? Max { get; set; }
    }

    public class RequestElpasedTimeMonitor<T, TR>
    {
        private readonly MonitorAction<T> _after;
        private readonly MonitorAction<T> _before;

        public RequestElpasedTimeMonitor(string name, TimeSpan maxTimeout, IRxnHealthManager rxn)
        {
            var watcher = new ElapsedTimePulsar<T>(name, _ => maxTimeout, h => rxn.Publish(h));
            _before = watcher.Before();
            _after = watcher.After();
        }

        public IPreRequestHandler<T> StartTimer()
        {
            return new PreHandlerAction<T>(_ => _before.Do(_));
        }

        public IPostRequestHandler<T, TR> EndTimer()
        {
            return new PostHandlerAction<T, TR>((_, __) => _after.Do(_));
        }
    }

    public class PostHandlerAction<T, TR> : IPostRequestHandler<T, TR>
    {
        private readonly Action<T, TR> _start;

        public PostHandlerAction(Action<T, TR> start)
        {
            _start = start;
        }
        public void Handle(T request, TR response)
        {
            _start(request, response);
        }
    }

    public class PreHandlerAction<T> : IDomainCommandPreHandler<T>
    {
        private readonly Action<T> _start;

        public PreHandlerAction(Action<T> start)
        {
            _start = start;
        }

        public void Handle(T message)
        {
            _start(message);
        }
    }

    public class ElapsedTimePulsar<T> : IDisposable, IMonitorActionFactory<T>
    {
        private readonly string _name;
        private readonly Func<T, TimeSpan?> _maxExpected;
        private readonly Action<IHealthEvent> _metrics;
        private readonly Stopwatch _speed;

        public ElapsedTimePulsar(string name, Func<T, TimeSpan?> maxExpected, Action<IHealthEvent> metrics)
        {
            _name = name;
            _maxExpected = maxExpected;
            _metrics = metrics;
            _speed = new Stopwatch();
        }

        public MonitorAction<T> Before()
        {
            return new MonitorAction<T>()
            {
                When = _ => true,
                Do = _ =>
                {
                    _speed.Start();
                }
            };
        }

        public MonitorAction<T> After()
        {
            return new MonitorAction<T>()
            {
                When = _ => true,
                Do = req =>
                {
                    _speed.Stop();

                    _metrics(new AppOpTimer()
                    {
                        Name = _name,
                        Speed = _speed.Elapsed,
                        Max = _maxExpected(req)
                    });

                    _speed.Reset();
                }
            };
        }

        public void Dispose()
        {
        }
    }
}
