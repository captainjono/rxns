using System;
using System.Diagnostics;

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

        public static Func<string> CallingTypeNameImpl = () =>
        {
            var callerMethod = new StackFrame(3).GetMethod();

            if (callerMethod == null) return "Unknown";

            var declaringType = callerMethod.DeclaringType;
            if (declaringType == null) return $"?:{callerMethod.Name}";

            return $"{declaringType.DeclaringType?.Name ?? ""}:{declaringType.Name}:{callerMethod.Name}";
        };
    }
}
