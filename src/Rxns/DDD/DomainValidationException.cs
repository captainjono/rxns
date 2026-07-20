using System;

namespace Rxns.DDD
{
    public class DomainValidationException : Exception
    {
        public DomainValidationException() : base() { }
        public DomainValidationException(string message, params string[] args) : base(message.FormatWith(args)) { }
    }
}
