using System.Collections.Generic;

namespace Rxns.Interfaces
{
    /// <summary>
    /// This interface represents the different types of comminication requirements of system.
    /// The achriecture of the system is a star, with a central rxn hub that events can be routed
    /// via to target remote hosts. This should be used in combination with the auto-routing
    /// backing channel
    /// </summary>
    public interface IRxnManagerRegistry
    {
        /// <summary>
        /// Channel used for local-in-process events.
        /// </summary>
        IRxnManager<IRxn> RxnsLocal { get; }
        /// <summary>
        /// Channel used for rxn exchange with "the server". The server addresses us
        /// buy our IRouteProvider.GetLocalBaseRoute(). This channel is slower because
        /// all messages are garenteed to be delivered once, in the order they are received. 
        /// In cases of downtime, messages  are cached locally until communication can be re-established
        /// </summary>
        IRxnManager<IRxn> RxnsCentralReliable { get; }
        /// <summary>
        /// Channel used for rxn exchange with "the server". The server addresses
        /// us buy our IRouteProvider.GetLocalBaseRoute(). This channel is faster but
        /// and while the messages will make it 99% of the time, there is no implicit garentee
        /// they will. In cases of downtime, messages are retried and then lost.
        /// </summary>
        IRxnManager<IRxn> RxnsCentral { get; }

        /// <summary>
        /// A map of address/route => Channel for clients that have been discovered by
        /// our client via some mechanism.
        /// note: Not sure what this is used for as YET :)
        /// </summary>
        IDictionary<string, IRxnManager<IRxn>> ClientRoutes { get; }
    }
}
