using System;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    public class DomainCommandException : Exception
    {
        public string DomainMessage { get; set; }




        public DomainCommandException(IDomainCommand command, string message) : base($"{command.Id} : {message}")
        {
            DomainMessage = message;
        }

        public DomainCommandException(string message)
            : this(null, message)
        {
        }
    }
}
