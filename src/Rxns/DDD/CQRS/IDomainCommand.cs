using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    public interface IDomainCommand : IServiceCommand
    {
        
    }

    public interface IDomainCommand<T> : IAsyncRequest<DomainCommandResult<T>>, IDomainCommand
    {
        string Id { get; }
        //void MarkAsSucess();
        //void MarkAsFailure(IList<string> errors);
    }
}
