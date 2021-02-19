# SpareReactors

* These essentially represent `scaling` units in your *micro-app-mesh*. 
* You can spawn these and connect these to a [ClusterHost](clusterhost.md) 
* The ClusterHost will *update* the *SpareReactor* to the **choosen** `scale-out App` automatically whenever it connects

  ```c#

  new ClusteredAppHost().ConfigureWith(
        new AutoScaleoutReactorPlan( //the auto scaling host
            new ScaleoutToEverySpareReactor(),  //logic to control how the scale-out will occour
            "AnyApp", //the app to scale
            "reactorName | null",  //the reactorname of the app to sale, or null, to scale the whole app
            "version | null") //the version of the App to scale as uploaded to the AppUpdateManager
            )
        )
  ```

  `picture of sparereactor on appstatus portal`

# Patterns & Practices

* This pattern is mostly useful for cases where Cloud scaleout is not appropriote or inefficent. This may be because you need more control over your scaling practices or because you may want to experiment with multi-plexiing different workloads on different `bare metal` or `vm hosts` to *get more bang for your cloud buck*.

* Deploy `SpareReactors` directly from [Docker Hub](https://hub.docker.com) or one customised to your needs