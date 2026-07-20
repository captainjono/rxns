using System;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    public class DomainQueryMediator : Mediator, IDomainQueryMediator
    {
        public DomainQueryMediator(SingleInstanceFactory singleInstanceFactory) : base(singleInstanceFactory)
        {
        }

        public override Type Handles()
        {
            return typeof(IDomainQueryHandler<,>);

        }
    }

    public interface IDomainQueryMediator : IMediator
    {
    }
}
