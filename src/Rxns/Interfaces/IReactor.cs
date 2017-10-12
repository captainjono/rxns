using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using Rxns.Health;

namespace Rxns.Interfaces
{
    /// <summary>
    /// An atmoic, isolated rxn driven world. A place to let your reactions
    /// run wild, without interfearing with the mechanics of other reactions
    /// in your system.
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    public interface IReactor<TEvent> : IReactTo<TEvent>, IReportStatus, IReportHealth
    {
        /// <summary>
        /// The name of the reactor
        /// </summary>
        string Name { get; set; }
        
        /// <summary>
        /// All the reactants that have been chained or attached to this reactor
        /// </summary>
        IEnumerable<object> Molecules { get; }

        /// <summary>
        /// A stream of update-to-date health information for the reactor
        /// </summary>
        IObservable<IReactorHealth> Health { get; }

        /// <summary>
        /// Stops and disposes of all connections
        /// </summary>
        void Stop();

        /// <summary>
        /// The monitor should do things like
        /// - watch for things like errors being broadcast,
        /// - watch for over-stimulated reactors, and possible disconnect them for periods?
        /// </summary>
        /// <param name="doctor"></param>
        /// <returns></returns>
        IDisposable Monitor(IHealReators<TEvent> doctor);

        /// <summary>
        /// Connects an rxnManager to this reactor. The rxnManager subscription is piped
        /// to the input channel, and the output channel is piped to the eventManagers publish method
        /// which in essense connects this reactor to "the outside world"
        /// </summary>
        /// <param name="rxnManager"></param>
        /// <param name="input">Used for events that are received from the rxnManager</param>
        /// <param name="output">Used for events that are published on the output channel to the rxnManager</param>
        /// <returns></returns>
        IDisposable Chain(IRxnManager<TEvent> rxnManager, IScheduler input = null, IScheduler output = null);

        /// <summary>
        /// Connects another reactor as a child of this reactor such that its input
        /// and output channels are linked as well its its reporting channels.
        /// Any errors are propertgated up the chain and can be observed by the parent reactor.
        /// Use IreactorCfg on the child to configure how input and output channels
        /// are joined
        /// </summary>
        /// <param name="another"></param>
        IDisposable Chain<T>(IReactor<T> another) where T : TEvent;

        /// <summary>
        /// Hooks up and IrxnProcessor<T> to the reactor so it obseverves any TEvents that are seen
        /// on the input channel, boardcasting the results on the output channel
        /// 
        /// Note: rxnProcessors that implement the interface many times will only have a single
        /// input channel establed to the process method, switching reactively to the type of
        /// rxn observed and the appropriote process method.
        /// </summary>
        /// <param name="irxnProcessor"></param>
        /// <param name="inputScheduler">This is the scheduler used to serve the Process pipeline. If you are running
        /// many processors in parrallel, and one fails, if you are using the default/singlethreaded scheduler, the other processors
        /// wont actually recieve any events.  The taskpool scheduler is good for getting around this side-effect.</param>
        /// <returns></returns>
        IDisposable Connect(object irxnProcessor /*recovery mode ? */, IScheduler inputScheduler = null);

        /// <summary>
        /// Hooks up a eventPublisher to the reactor such that its publish method broadcastes events
        /// on the reactors Output channel 
        /// </summary>
        /// <param name="publisher"></param>
        /// <returns></returns>
        IDisposable Connect(IRxnPublisher<TEvent> publisher);

        /// <summary>
        /// Hooks up a reaction to the this reactor joining the input and output channels together
        /// Note: you can use the IReactionCfg interface to configure the details of his this happens
        /// on the Connected object.
        /// </summary>
        /// <param name="reaction"></param>
        /// <param name="inputScheduler">This is the scheduler used to serve the Input pipeline. If you are running
        /// many processors in parrallel, and one fails, if you are using the default/singlethreaded scheduler, the other processors
        /// wont actually recieve any events.  The taskpool scheduler is good for getting around this side-effect.</param>
        /// <returns></returns>
        /// <returns></returns>
        IDisposable Connect(IReactTo<TEvent> reaction, IScheduler inputScheduler = null);


    }
}
