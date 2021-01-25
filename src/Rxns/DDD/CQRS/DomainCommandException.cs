using System;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    public class DomainCommandException : Exception
    {
        public string DomainMessage { get; set; }




        public DomainCommandException(IDomainCommand command, string message, params object[] args) : base("{0}: {1}".FormatWith(command.GetType().Name, message.FormatWith(args)))
        {
            DomainMessage = message.FormatWith(args);
        }

        public DomainCommandException(string message, params object[] args)
            : base(message.FormatWith(args))
        {
            DomainMessage = message.FormatWith(args);
        }
    }
}
