using System.Collections.Generic;
using Rxns.Reliability.Utilities;

namespace Rxns.Reliability
{
    /// <summary>
    /// A readonly dictionary of string key / object value pairs
    /// </summary>
    public class Context : ReadOnlyDictionary<string, object>
    {
        internal static Context Empty = new Context(new Dictionary<string, object>());

        internal Context(IDictionary<string, object> values)
            : base(values)
        {
        }
    }
}