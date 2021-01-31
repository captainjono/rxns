# Scaling

Reactions might come across as a high-level framework, but once of its philisophical design goals was to make scaling *easier* for the average App to take advantage of multi-processor, multi-host, cloud native technology.
<!-- TOC -->

1. [Scaling](#scaling)
   1. [How?](#how)
   2. [Features](#features)
      1. [InProcess scaleout](#inprocess-scaleout)
      2. [OutOfProcess scaleout](#outofprocess-scaleout)
      3. [Cloud scaleout](#cloud-scaleout)

<!-- /TOC -->
## How? 
Reactions acheives this goal by stealth most of the time.** ***But you need to understand** *some things** that effect scaling... The main thing one being, state.

>Note: Where those state mutations happen, and how the rest of your system uses those state mutations is where the majority of your effort lies while using reactions.

Scaling can be broken up into a few different categories:

## Features

Not all Apps need scaling. At first, its recommended to concentrate on getting your Domain model and state transitions right. Once that is done, and you are performance testing your App, you can then start to think about what aspects require scaling.

### InProcess scaleout

This is the default operating behaviour of your app. In this case, you are simply taking advantage of Reactions [building blocks](buildngblocks.md) to acheive in-app partionining for various use-cases:
- a. In-process tenant'ed isolation. This is where your Apps can process tenanted information internally, in different threads. This helps to ensure the integrity of your tenented App.
        These components include:
    * `ShardingQueueProcessor`: A queue which can be serviced by N-number of worker threads in parallel
    * [Reactors](reactors.md): A isolation mechanism to seperate your App into Micro-components that can be reasoned about individually
    * (*more examples to come*) ...

### OutOfProcess scaleout

In this type of scaleout, your App will be executed multiple times, concurrently in an effort to fully utilise the hosts cores, no matter the count.

>NOTE: Complexity lies when scaling is in operation. Care must be taken to understand that this means that all the components of your App cannot *assume* they have direct access to all resources, in the same process.

- a. By using the [ClusteredAppHost](rxnhosts.md) to AND by using the [reactor pattern](reactors.md), you can scale:
  1. *Your entire App* into multiple processes.
  1. *Specific* reactors in your App into multiple processes
  2. *Specific* reactors in your App onto multiple *hosts*
  3. *Specific* QueueWorkers of your App into worker processes
  4. *Specific* QueueWorkers of your App onto *other hosts*

- b. Use the underling scale-APIs directly, you can customise the scaling experience to suite your specific needs

### Cloud scaleout

In the days of abstracted cloud infrstructure, technologies like [Docker / AKS](cicd.md) and Cloud Functions is becoming a prefered scaleout for the general use-case over traidtional self-managed solutions. But these technologies can be over-kill for Dev/Test/Apps that are yet to hit there hockey-stick growth.

With these additional steps, reactions let you take advantage of these technoligies with the same configuration and mental model as OutOfProcess scaleout.
1. Configure your scaleout options to `Cloud`
2. Setup your cloud provider credentials
3. ...profit!


