using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Playback;

namespace Rxns.DDD
{
    public class DDDServerModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp<TapeArrayFactory>()
                .CreatesOncePerApp<InMemoryTapeRepo>(true)
                .CreateGenericOncePerAppAs(typeof(TenantModelFactory<>), typeof(ITenantModelFactory<>))
                //.CreatesOncePerApp<Inmemory>()
                .CreatesOncePerApp<LocallyExecutingCommandService>()
                .CreatesOncePerApp<SingleInstanceFactory>(c =>
                {
                    return type => c.Resolve(type);
                })
                .CreatesOncePerApp<DomainCommandMediator>()
                .CreatesOncePerRequest<DomainQueryMediator>()
                .CreateGenericOncePerAppAs(typeof(DomainCommandPipeline<,>), typeof(IRxnMediatorPipeline<,>))
                .CreateGenericOncePerAppAs(typeof(DomainQueryPipeline<,>), typeof(IRxnMediatorPipeline<,>))

                //.CreateGenericOncePerAppAs(typeof(DomainModelChangesPublisher<,>), typeof(IDomainCommandPostHandler<,>))
                //.CreateGenericOncePerAppAs(typeof(QueryResultPublisher<,>), typeof(IDomainCommandPostHandler<,>))
                ;
        }
    }
}
