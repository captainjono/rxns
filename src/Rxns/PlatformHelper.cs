using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns
{
    /// <summary>
    /// A helper class used to wrap operations that may not exist on every .Net platform
    /// </summary>
    public static class PlatformHelper
    {
        /// <summary>
        /// Returns the name of the method that called this function.
        /// Override the static CallingTypeNameImpl to support this feature if your platform supports it
        /// </summary>
        public static string CallingTypeName { get { return CallingTypeNameImpl(); } }

        public static Func<string> CallingTypeNameImpl = () => "unknown";
    }
}
