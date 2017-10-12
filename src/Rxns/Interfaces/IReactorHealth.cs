using Rxns.Logging;

namespace Rxns.Interfaces
{
    /// <summary>
    /// The current state of a reactor
    /// </summary>
    public enum HealthStatus
    {
        Ok,
        Recovering,
        Error,
        Crashed
    }

    /// <summary>
    /// Describes the working state of a reactor to
    /// interested supervisors
    /// </summary>
    public interface IReactorHealth
    {
        /// <summary>
        /// The current state
        /// </summary>
        HealthStatus Status { get; }
        /// <summary>
        /// A human readable description of the reactors state
        /// </summary>
        string Description { get; }
        /// <summary>
        /// The last error a reactor encourntered, either terminal
        /// or transient
        /// </summary>
        ErrorReport LastError { get; }
    }
}
