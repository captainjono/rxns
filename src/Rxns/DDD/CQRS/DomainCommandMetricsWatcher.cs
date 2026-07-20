using System;
using Rxns.DDD.Commanding;
using Rxns.Health;

namespace Rxns.DDD.CQRS
{
    public class DomainCommandRunning : HealthEvent
    {
        public long Count { get; set; }
        public TimeSpan Average { get; set; }
        

        public DomainCommandRunning(string reporter) : base(reporter)
        {

        }
    }

    public class DomainQueryRunning : HealthEvent
    {
        public long Count { get; set; }
        public TimeSpan Average { get; set; }

        public DomainQueryRunning(string reporter) : base(reporter)
        {

        }
    }

    public interface IRxnHealthManager
    {
        void Publish(IHealthEvent pulse);
    }


    //move watcher function into

    public class DomainCommandMetricsWatcher<T> : IDomainCommandPreHandler<T>
    {
        private readonly IRxnHealthManager _health;
        private readonly StreamCounterPulsar<T> watcher;
        private readonly MonitorAction<T> monitorHandleCalls;

        public DomainCommandMetricsWatcher(IRxnHealthManager health)
        {
            _health = health;
            var name = typeof(T).Name;
            watcher = new StreamCounterPulsar<T>(count => _health.Publish(new DomainCommandRunning($"Cmd-{name}") {  Count = count }), TimeSpan.FromSeconds(10));
            monitorHandleCalls = watcher.Before();
        }
        
        public void Handle(T message)
        {
            //todo convert into factory
            if(monitorHandleCalls.When(message)) monitorHandleCalls.Do(message);
        }
    }

    public class DomainQueryMetricsWatcher<T> : IDomainQueryPreHandler<T>
    {
        private readonly IRxnHealthManager _health;
        private readonly StreamCounterPulsar<T> watcher;
        private readonly MonitorAction<T> monitorHandleCalls;

        public DomainQueryMetricsWatcher(IRxnHealthManager health)
        {
            _health = health;
            var name = typeof(T).Name;
            watcher = new StreamCounterPulsar<T>(count => _health.Publish(new DomainQueryRunning($"Cmd-{name}") { Count = count }), TimeSpan.FromSeconds(10));
            monitorHandleCalls = watcher.Before();
        }

        public void Handle(T message)
        {
            //todo convert into factory
            if (monitorHandleCalls.When(message)) monitorHandleCalls.Do(message);
        }
    }
}
