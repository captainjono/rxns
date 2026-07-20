using System;

namespace Rxns.DDD.Commanding
{
    public class ServiceCommandNotFound : Exception
    {
        public ServiceCommandNotFound(string message, params object[] formatWith) : base(message.FormatWith(formatWith)) { }
    }
}
