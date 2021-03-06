﻿using Rxns.DDD.Commanding;

namespace Rxns
{
    /// <summary>
    /// An object which answers a question for async operation.
    /// Rxns involve events, and events are async by nature. When you require a response
    /// to an rxn you have triggered, this becomes a "question" 
    /// </summary>
    public interface IRxnQuestion : IServiceCommand
    {
        /// <summary>
        /// The source who asked the question. Any events triggered as a result of this
        /// should implement IRxnResponse and specify this in .AsResultOf(source)
        /// </summary>
        string AskedBy { get; }

        string Options { get; set; }
        string Destination { get; set; }
    }
}
