
# Reaction hosts

Hosts provide the execution environment for a reaction. You either adapt your existing *idea of a host* to the IRxnHost interface, or use drink the Reaction cool-aid and use one of the provided hosts:

Host|When|Example usage
-|-|-
ConsoleHost | Your app runs in the `console` / `terminal` native to the platform | Micro-services, Consumer Apps, debugging
WebApiHost | Your app need to be exposed via a WebApi | Micro-services, Web Apps, Consumer Apps, remote debugging
ClusteredAppHost | Your app needs to scale automatically to the resource of the computer | Micro-services, Consumer Apps, Web Apps, LOB
XamarinHost | Your app needs to run on mobile devices | Consumer apps, LOB