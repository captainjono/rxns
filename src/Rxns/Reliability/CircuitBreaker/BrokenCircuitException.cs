using System;

namespace Rxns.Reliability.CircuitBreaker
{
    /// <summary>
    /// Exception thrown when a circuit is broken.
    /// </summary>
    public class BrokenCircuitException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
        /// </summary>
        public BrokenCircuitException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public BrokenCircuitException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner.</param>
        public BrokenCircuitException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}