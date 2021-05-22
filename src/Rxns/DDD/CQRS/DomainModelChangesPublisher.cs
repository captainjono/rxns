using System;
using System.Collections.Generic;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.DDD.CQRS
{
    public class DomainModelChangesPublisher<T, TR> : IDomainCommandPostHandler<T, TR>
    {
        private readonly IRxnManager<IRxn> _rxnManager;

        public DomainModelChangesPublisher(IRxnManager<IRxn> rxnManager)
        {
            _rxnManager = rxnManager;
        }

        public void Handle(T request, IDomainCommandResult<TR> response)
        {
            _rxnManager.Publish(response);

            response.SideEffects.ForEach(e => _rxnManager.Publish(e).Until());
        }
    }

    public class DomainQueryResult<T> : IRxnResult
    {
        public T Result { get; set; }
        public DomainQueryResult(string inResponseTo, T result)
        {
            InResponseTo = inResponseTo;
            Result = result;
        }

        public string InResponseTo { get; }

        public override string ToString()
        {
            return $"{InResponseTo}:{Result}";
        }
    }

    public class QueryResultPublisher<T, TR> : IDomainQueryPostHandler<T, TR> where T : IDomainQuery<T>
    {
        private readonly IRxnManager<IRxn> _rxnManager;

        public QueryResultPublisher(IRxnManager<IRxn> rxnManager)
        {
            _rxnManager = rxnManager;
        }

        public void Handle(T request, TR response)
        {
            _rxnManager.Publish(new DomainQueryResult<TR>(request.Id, response));
        }
    }
}
