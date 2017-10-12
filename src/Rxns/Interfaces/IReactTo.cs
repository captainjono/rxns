using System.Reactive.Subjects;

namespace Rxns.Interfaces
{
    /// <summary>
    /// A human interface for a component which changes its state over
    /// time in reaction to stuff happening in the outside world
    /// </summary>
    /// <typeparam name="TStuff"></typeparam>
    public interface IReactTo<TStuff>
    {
        /// <summary>
        /// A way to funnel stuff into the reactor or to peer into the stuff 
        /// thats effecting its world
        /// </summary>
        ISubject<TStuff> Input { get; }
        /// <summary>
        /// The side-effects of the stuff that are input into this components world
        /// </summary>
        ISubject<TStuff> Output { get; }
    }
}
