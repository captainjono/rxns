using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// This interface is used in conjuction with IrxnProcessor/Publisher/IReactTo interfaces
    /// as a configuration mechanism for how the reactions are setup. If you implement one of the
    /// these interfaces, and dont specifiy this interface, the reactions will be hooked up to 
    /// the "default" system reaction using its existing setup.
    /// </summary>
    public interface IRxnCfg
    {
        /// <summary>
        /// The name of the reactor that this reaction will be hooked up to. 
        /// Null specifies the system will use the default reactor
        /// </summary>
        string Reactor { get; }
        /// <summary>
        /// Configures the input pipeline that is feeds any reations implemented by this
        /// class/interface. returning the pipeline fed into this method is the equivilant
        /// of not doing anything to it.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <returns></returns>
        IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline);
        /// <summary>
        /// The delivery scheme used to control the events that are observed on the input pipeline. 
        /// Null disables using any delivery scheme which.
        /// </summary>
        IDeliveryScheme<IRxn> InputDeliveryScheme { get; }

        bool MonitorHealth { get; }
    }
}
