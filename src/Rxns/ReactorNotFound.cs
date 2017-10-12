using System;

namespace Rxns
{
    /// <summary>
    /// Represents a request for a reactor that is not found in the system
    /// </summary>
    public class ReactorNotFound : Exception
    {
        public ReactorNotFound(string reactor) : base("The reactor '{0}' was not found".FormatWith(reactor)) { }
    }
}
