using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns;
using Rxns.Collections;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Interfaces.Reliability;
using Rxns.Logging;
using Rxns.Reliability;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantAndUserPollCfg
    {
        bool PollUsers { get; }
        bool PollTenants { get; }
    }

    public class FullyEnabledCurrentTenantAndUserPollCfg : ITenantAndUserPollCfg
    {
        public bool PollUsers
        {
            get { return true; }
        }

        public bool PollTenants
        {
            get { return true; }
        }
    }

    public class LegacyDbCurrentTenantAndUserEventSourcedCache : ReportStatusService, ICurrentTenantsService, ICurrentUsersService
    {
        private readonly ITenantAndUserPollCfg _cfg;
        //private readonly ITenantConfigurationService _tenantRepo;
        private readonly ITenantDatabaseFactory _dbFactory;
        private readonly IReliabilityManager _reliably;
        private readonly TimeSpan _pollInteRxnal;
        protected readonly ReplaySubject<string[]> _activeTenants = new ReplaySubject<string[]>();
        
        private readonly Dictionary<string, BehaviorSubject<string[]>> _tenantUserStreams = new Dictionary<string, BehaviorSubject<string[]>>();
        private readonly IDictionary<string, string[]> _userCache;
        private readonly IScheduler _polling;
        public IObservable<string[]> ActiveTenants { get { return _activeTenants; } }

        public LegacyDbCurrentTenantAndUserEventSourcedCache(ITenantAndUserPollCfg cfg, ITenantDatabaseFactory dbFactory, IReliabilityManager reliably, IDictionary<string, string[]> userCache = null, TimeSpan? pollInteRxnal = null, IScheduler polling = null)
        {
            _cfg = cfg;
          //  _tenantRepo = tenantRepo;
            _dbFactory = dbFactory;
            _reliably = reliably;
            _userCache = userCache ?? new Dictionary<string, string[]>();
            _polling = polling ?? Scheduler.Default;
            _pollInteRxnal = pollInteRxnal ?? TimeSpan.FromMinutes(15);
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Run(() =>
            {
                if (_cfg.PollTenants)
                    Observable.Timer(TimeSpan.FromSeconds(0), _pollInteRxnal, _polling)
                            .SelectMany(_ => ConsolidateActiveTenantsAndUsers())
                            .Until(OnError)
                            .DisposedBy(_onStop);
            });
        }

        private bool _isFirstPoll = true;
        private IObservable<Unit> ConsolidateActiveTenantsAndUsers()
        {
            return Rxn.DfrCreate(() =>
            {
                string[] active = null;

                var existing = _isFirstPoll ? new string[] { } : _activeTenants.Value();
                _isFirstPoll = false;

                var current = new string[] {"main" };

                if (existing.Length != current.Length /*quick diff */ || existing.Except(current).AnyItems() /*accurate diff */)
                {
                    OnInformation("Current tenants: {0}", current.ToStringEach());
                    active = current;
                }

                if (active == null) return new Unit().ToObservable();

                return ConsolidateUsersForTenants(current);
            });
        }

        private IObservable<Unit> ConsolidateUsersForTenants(string[] tenants)
        {
            return tenants
                .ToObservable(_polling)
                .SelectMany(t => _cfg.PollUsers ? ConsolidateUsersForTenant(t) : Observable.Empty<KeyValuePair<string, string[]>>())
                .Do(users =>
                {
                    if (users.Value.Length > 0)
                        CurrentThreadScheduler.Instance.Run(() =>
                        {
                            var usrs = users;
                            _tenantUserStreams[usrs.Key].OnNext(usrs.Value);
                        }); //trampoline so our Service is uneffected by subscriptions
                })
                .Select(_ => new Unit())
                .FinallyR(() =>
                {
                        //alert on new tenants only after all users have been alerted as this is how the API is utilised "foreach tenant, lookup users"
                        CurrentThreadScheduler.Instance.Run(() => _activeTenants.OnNext(tenants));
                });
        }

        private IObservable<KeyValuePair<string, string[]>> ConsolidateUsersForTenant(string tenant)
        {
            return Rxn.Create<KeyValuePair<string, string[]>>(o =>
            {
                if (!_tenantUserStreams.ContainsKey(tenant))
                    _tenantUserStreams.Add(tenant, new BehaviorSubject<string[]>(new string[] { })); //todo: implement remove when tenant is deleted

                if (!_userCache.ContainsKey(tenant))
                    _userCache.Add(tenant, new string[] { });

                var existing = _userCache[tenant];

                OnVerbose("[{0}] Looking up users", tenant);
                    //lookup users and log them
                    return _reliably.CallWithPolicyForever(() => _dbFactory.GetUsersContext(_dbFactory.GetContext(tenant)).GetUsers(), ReliabilityManager.AnyErrorStratergy)
                                        .Select(current =>
                                        {
                                    _userCache[tenant] = current;

                                    if (existing.Length != current.Length /*quick diff */|| existing.Except(current).AnyItems() /*accurate diff */)
                                    {
                                        OnInformation("[{0}] Current users: {1}", tenant, current.Length);
                                        return new KeyValuePair<string, string[]>(tenant, current);
                                    }

                                    return new KeyValuePair<string, string[]>(tenant, new string[0]);
                                })
                                        .Subscribe(o);
            });
        }

        public IObservable<string[]> CurrentUsers(string tenant)
        {
            if (!_tenantUserStreams.ContainsKey(tenant)) throw new UnknownTenantException(tenant);

            return _tenantUserStreams[tenant];
        }

        public IObservable<IRxn> Process(TenantStatusChangedEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                OnInformation("{0} tenant '{1}'", @event.IsActive ? "Activating" : "De-activating", @event.Tenant);
                if (@event.IsActive)
                    ConsolidateUsersForTenants(_activeTenants.Value().Concat(new[] { @event.Tenant }).ToArray()).Until(OnError);
                else
                    ConsolidateUsersForTenants(_activeTenants.Value().Except(new[] { @event.Tenant }).ToArray()).Until(OnError);
            });
        }
    }
}
