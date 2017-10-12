using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// Represents a reactors connection status in the system
    /// </summary>
    public class ReactorConnection
    {
        /// <summary>
        /// If the reactor connected, this value will not be null and can be used
        /// disconnect the reactor. If you disconnect it please null this just after :)
        /// </summary>
        public IDisposable Connection { get; set; }
        /// <summary>
        /// The reactor that this state reprents
        /// </summary>
        public Rxns.Interfaces.IReactor<IRxn> Reactor { get; set; }
    }

}
