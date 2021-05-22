using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Rxns.DDD.Tenant;
using Rxns.Playback;

namespace Rxns.DDD.BoundedContext
{
    public class TapeArrayTenantModelRepository<T, TR> : ITenantModelRepository<T> where T : IAggRoot, new() where TR : IDomainEvent
    {
        private readonly ITapeArray<TR> _repo;
        private readonly ITenantModelFactory<T> _dmFactory;
        private string _repoRoot;

        public TapeArrayTenantModelRepository(ITapeArrayFactory repo, ITenantModelFactory<T> dmFactory, Func<TR, string> tapeSelector)
        {
            _repoRoot = $".\\{typeof(T).Name}";
            _repo = repo.Create<TR>(_repoRoot, tapeSelector); //files named StudentID in tape array
            _dmFactory = dmFactory;
        }

        public IObservable<T> GetById(string tenant, string id)
        {
            return GetEventsFromRepo(tenant, id).ToArray().Select(events =>
            {
                var model = _dmFactory.Create(tenant, id, events);

                return model;
            });
        }

        private IObservable<IDomainEvent> GetEventsFromRepo(string tenant, string id)
        {
            return _repo.GetOrCreate(AsKey(tenant, id)).Source.Contents.Select(s => s.Recorded as IDomainEvent)
                .Where(s => s != null);
        }
        
        public IEnumerable<IDomainEvent> Save(string tenant, T entity)
        {
            var uncommitted = entity.GetUncommittedChanges().ToArray();
            Save(tenant, entity, uncommitted);
            return uncommitted;
        }

        public void Save(string tenant, T entity, IEnumerable<IDomainEvent> uncommitted)
        {
            var context = _repo.GetOrCreate(AsKey(tenant, entity.EId));
            //entity.ThrowValidationExceptions(); //ensure entity is valid before saving
            
            using (var entityRepo = context.Source.StartRecording())
            {
                foreach (var @event in uncommitted)
                {
                    //should wrap in a transaction
                    entityRepo.Record(@event);
                    entity.MarkChangesAsCommitted(@event);
                }
            }

            //_changes.OnNext(new EventsOccured()
            //{
            //    Id = entity.EId,
            //    Tenant = tenant,
            //    Events = changes
            //});
        }

        private string AsKey(string tenant, string id)
        {
            return $"{_repoRoot}\\{tenant}_{id}";
        }
    }
}
