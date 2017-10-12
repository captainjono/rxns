using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// A health check / supvisor service which monitors
    /// reactors for failure, and heals them in a methodical 
    /// way
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IHealReators<T>
    {
        /// <summary>
        /// Commences montior of the reactor provided
        /// </summary>
        /// <param name="reactor">The reactor whos health i will maintain</param>
        /// <returns>A resources which stops the monitoring operation</returns>
        IDisposable Monitor(IReactor<T> reactor);
    }
}
