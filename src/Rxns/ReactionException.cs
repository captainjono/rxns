using System;

namespace Rxns
{
    /// <summary>
    /// An unhandled exception that occours in response to a stuff happening
    /// </summary>
    public class ReactionException : Exception
    {
        public ReactionException(Exception inner, string messageFormat, params object[] formatter) : base(messageFormat.FormatWith(formatter), inner) { }
    }
}