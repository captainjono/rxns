using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health.AppStatus
{
    public interface ISystemStatusStore : IObservable<Dictionary<SystemStatusEvent, object[]>>
    {
        IObservable<bool> AddOrUpdate(SystemStatusEvent status, dynamic[] meta);
        IObservable<dynamic[]> GetSystemStatus(string tenant, string system, string version);
        IObservable<Dictionary<SystemStatusEvent, dynamic[]>> GetAllSystemStatus();
    }

    public class SystemSystemStatusStore : ISystemStatusStore
    {
        public class StringStore
        {
            public string j { get; set; }
        }

        private readonly IKeyValueStore<string, StringStore> _backingStoreV;
        private readonly IKeyValueStore<string, StringStore> _backingStoreK;
        private readonly Subject<Dictionary<SystemStatusEvent, dynamic[]>> _notificationChannel = new Subject<Dictionary<SystemStatusEvent, dynamic[]>>();

        public SystemSystemStatusStore(IKeyValueStore<string, StringStore> backingStoreK, IKeyValueStore<string, StringStore> backingStoreV)
        {
            _backingStoreV = backingStoreV;
            _backingStoreK = backingStoreK;
        }

        public IObservable<bool> AddOrUpdate(SystemStatusEvent status, dynamic[] meta)
        {
            var key = AsKey(status);
            return _backingStoreK.AddOrUpdate(key, new StringStore() {j = status.Serialise()})
                .Concat(_backingStoreV.AddOrUpdate(key, new StringStore() {j = meta.Serialise()})
                    .Do(_ =>
                    {
                        _notificationChannel.OnNext(new Dictionary<SystemStatusEvent, dynamic[]>()
                        {
                            {status, meta}
                        });
                    }))
                .LastOrDefaultAsync();
            
        }

        private string AsKey(SystemStatusEvent status)
        {
            return AsKey(status.Tenant, status.SystemName, status.Version);
        }

        private string AsKey(string tenant, string system, string version)
        {
            return string.Format("{0}_{1}_{2}", tenant, system, version);
        }

        public IObservable<dynamic[]> GetSystemStatus(string tenant, string system, string version)
        {
            return _backingStoreV.Get(AsKey(tenant, system, version)).Select(r => r.j.Deserialise<dynamic[]>());
        }

        /// <summary>
        /// Every system's status, keyed by the system that reported it.
        ///
        /// <para>Status lives in two stores and this walks the value store, looking each record up
        /// in the key store. Those writes are not atomic with respect to each other, so a value
        /// record can be read before its key record lands - a torn read, which the next status
        /// publish resolves on its own. A torn read must therefore be skipped, not thrown on: this
        /// method is subscribed to (see <see cref="Subscribe"/>), so anything it throws terminates
        /// the status stream and takes command routing down with it. The arena then answers every
        /// result with "NO ROUTE" and callers hang on work that already succeeded, several minutes
        /// and one subsystem away from the cause.</para>
        /// </summary>
        public IObservable<Dictionary<SystemStatusEvent, dynamic[]>> GetAllSystemStatus()
        {
            var dict = new Dictionary<SystemStatusEvent, dynamic[]>();

            foreach (var record in _backingStoreV.GetAll().Wait())
            {
                try
                {
                    var split = record.Key.Split('_');
                    if (split.Length < 3) continue;

                    var key = AsKey(split[0], split[1], split[2]);
                    var keyRecord = _backingStoreK.Get(key).Wait()?.j.Deserialise<SystemStatusEvent>();
                    if (keyRecord == null) continue;

                    // Indexer, not Add: two records whose key deserialises equal is a duplicate
                    // report, not a reason to fail the whole snapshot.
                    dict[keyRecord] = record.Value.j.Deserialise<dynamic[]>();
                }
                catch (Exception e)
                {
                    // One unreadable record must not cost the caller every other system's status.
                    $"Skipped status record '{record.Key}': {e.Message}".LogDebug();
                }
            }

            return dict.ToObservable();
        }

        public IDisposable Subscribe(IObserver<Dictionary<SystemStatusEvent, dynamic[]>> susbcribe)
        {
            return Observable.Create<Dictionary<SystemStatusEvent, dynamic[]>>(o =>
                {
                    var sub1 = GetAllSystemStatus().Subscribe(s => o.OnNext(s));
                    var sub = _notificationChannel.Subscribe(s => o.OnNext(s));

                    return new CompositeDisposable(sub, sub1);
                })
                .Subscribe(susbcribe);
        }
    }
}
