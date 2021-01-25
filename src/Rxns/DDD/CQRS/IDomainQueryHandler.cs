using Rxns.DDD.Commanding;


namespace Rxns.DDD.CQRS
{
    public interface IDomainQueryHandler<T, TR> : IAsyncRequestHandler<T, TR>
        where T : IDomainQuery<TR>
    {

    }
}
