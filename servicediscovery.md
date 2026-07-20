# Service Discovery

Reactions provides a microservice registry which allows you to map `domainAPIs` to the endpoints which are used by configuration classes to configure your App on startup.

```c#

new SSDPDiscovery().DiscoverServices();
```

This registry can be configured statically in code, via the rxn.cfg or automatically via service discovery implemented using the `SSDP` protocal

# Automatic Detection

* Automatic detection means you spend less time configuring, and more time developing when building your App
* `SSDP` is suitable for local network situations like within `Dev/test` or with `Docker`
* **Dont use automatic detection on the open internet**

