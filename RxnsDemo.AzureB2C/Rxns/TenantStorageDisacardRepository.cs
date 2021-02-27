using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Rxns;
using Rxns.Collections;
using Rxns.DDD.BoundedContext;
using Rxns.Interfaces;
using Rxns.NewtonsoftJson;
using Rxns.Playback;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantFileStorage
    {
        Task<Stream> GetAsync(string tenantId, string location, string fileName);
        Task<IFileMeta[]> GetAllAsync(string tenantId, string location);
        Task<IFileMeta> SaveAsync(string tenantId, string location, IFileMeta file, SeekOrigin offset = SeekOrigin.Begin);
        Task<IFileMeta> GetMeta(string tenantId, string location, string fileName);
        Task<bool> DeleteAsync(string tenantId, string location, string fileName);
    }

    public class TenantStorageDiscardRepository : ITenantDiscardRepository
    {
        private readonly ITenantFileStorage _fileStore;

        public TenantStorageDiscardRepository(ITenantFileStorage fileStore)
        {
            _fileStore = fileStore;
        }

        public IObservable<Unit> DiscardPoisonEvent(IDomainEvent @event, Exception with)
        {
            var contents = @event.ToJson().ResolveAs(@event.GetType()).ToStream();
            return _fileStore.SaveAsync(@event.Tenant, "Discards", new InMemoryFile()
                {
                    Contents = contents,
                    ContentType = "application/json",
                    Name = @event.Id.ToString(),
                    LastWriteTime = @event.Timestamp,
                    Length = contents.Length
                })
                .ToObservable()
                .Concat(DiscardMeta(@event.Tenant, @event.Id.ToString(), with))
                .Select(_ => new Unit());
        }

        public IObservable<Unit> DiscardPoisonTape(string tenant, IFileTapeSource tape, Exception with)
        {
            return _fileStore.SaveAsync(tenant, "SyncDiscards", tape.File)
                .ToObservable()
                .Concat(DiscardMeta(tenant, tape.File.Name, with))
                .Select(_ => new Unit());
        }

        private IObservable<Task<IFileMeta>> DiscardMeta(string tenant, string discardName, Exception with)
        {
            return _fileStore.SaveAsync(tenant, "SyncDiscards", new InMemoryFile()
                {
                    Name = "{0}.error".FormatWith(discardName),
                    ContentType = "application/json",
                    Contents = with.ToString().ToStream()
                })
                .ToObservable();
        }
    }
}
