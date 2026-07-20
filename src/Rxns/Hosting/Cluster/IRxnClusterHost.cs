using System;
using System.Collections.Generic;

namespace Rxns.Hosting.Cluster
{
    /*
//cluster starts up and loads up its capacity config
//can program the expressions that trigger different things
//all programs have an inital capacity of 1 - which spawns a single process
* then scale units can be applied to each reactions, which configures the bias in the reactions
-rxnClusterConfig
  -single process 
  -single machine (multi-process mode that auto-scales based on units and spareReactors)
  -cloud process
-cloud processes will scale at using a stratergy that is the same which is used for single machine
  -can code that using using a orchestrator which takes can "add process to cluster" in the same way, cloud or multi-process
      -routing is setup the same way, using a serviceregistry and a routing table
          -semantics of the reactor are availble in the creator, so we known ahead of time what events we need to repeat into that process
* that will scale out to a different process to stop backpressure building up (ms performance)
-scaleOutStratergy
-isolated process
-cloud process
      -function app

*/

    public interface IRxnClusterHost : IRxnHost
    {
        IList<IRxnAppContext> Apps { get; }
        IRxnClusterHost ConfigureWith(IRxnAppScalingManager scalingManager);
        IObservable<IRxnAppContext> SpawnOutOfProcess(IRxnHostableApp rxn, string name, Type[] routes = null);
    }
}
