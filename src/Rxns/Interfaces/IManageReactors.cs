namespace Rxns.Interfaces
{
    /// <summary>
    /// Defines a set of operation used to th life-cycle of reactors and their reactons in the system
    /// </summary>
    public interface IManageReactors
    {
        /// <summary>
        /// Starts and connects a reactor to the specified parent or default reactor if none is
        /// specified.
        /// </summary>
        /// <param name="reactorName"></param>
        /// <param name="parent"></param>
        ReactorConnection StartReactor(string reactorName, IReactor<IRxn> parent = null);

        /// <summary>
        /// Disconnects the reactor from its parent, effectively isolating it and all its services
        /// from anymore events. This ooperation does not stop any of the services that are connected
        /// to the reactor, and maintains them incase you want to start it up again.
        /// </summary>
        /// <param name="reactorName"></param>
        void StopReactor(string reactorName);
    }

}
