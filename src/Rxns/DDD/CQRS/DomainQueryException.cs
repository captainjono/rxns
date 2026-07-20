using System;

namespace Rxns.DDD.CQRS
{
    public class DomainQueryException : Exception
    {
        public DomainQueryException(object command, string message, params object[] args)
            : base("{0}: {1}".FormatWith(command.GetType(), message.FormatWith(args)))
        {

        }

        public DomainQueryException(IDomainQuery command, string message, params object[] args)
            : base("{0}: {1}".FormatWith(command.GetType(), message.FormatWith(args)))
        {
            
        }
    }
}
