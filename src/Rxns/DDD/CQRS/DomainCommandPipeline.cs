using Rxns.DDD.CQRS;

namespace Rxns.DDD.Commanding
{
    public class DomainCommandPipeline<T, TR> : RxnMediatorPipeline<T, DomainCommandResult<TR>>, IDomainCommandHandler<T, TR> where T : IDomainCommand<TR>

    {
        public DomainCommandPipeline(IDomainCommandHandler<T, TR> inner, IDomainCommandPreHandler<T>[] preRequestHandlers, IDomainCommandPostHandler<T, TR>[] postRequestHandlers, IRxnHealthManager health)
            : base(inner, preRequestHandlers, postRequestHandlers, health)
        {
        }
    }

    public class DomainQueryPipeline<T, TR> : RxnMediatorPipeline<T, TR>, IDomainQueryHandler<T, TR> where T : IDomainQuery<TR>

    {
        public DomainQueryPipeline(IDomainQueryHandler<T, TR> inner, IDomainQueryPreHandler<T>[] preRequestHandlers, IDomainQueryPostHandler<T, TR>[] postRequestHandlers, IRxnHealthManager health)
            : base(inner, preRequestHandlers, postRequestHandlers, health)
        {
        }
    }

}
