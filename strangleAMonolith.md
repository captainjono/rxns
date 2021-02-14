# Strangle The Monolith

Often event sourcing can fit a need when it comes to reducing resource load on existing systems and components. Rxns provides a serious of components, patterns and practices which can help you to add event sourcing in a unobtrusive, peicemeal way to an existing code-base.


Pattern | Application
-|-
[RxnDecorator](rxndecorators.md) | Add event sourcing to existing APIs / objects without recompiling them
[ViewProcessor](ViewProcessors.md) | Deprecate databases / tables / legacy systems with strangler DatabaseViewProcessors. Or add caching infront of existing data stores or integrations which are struggling to scale with the rest of your system
[Reactor](reactors.md) | Create a seperate enclave where you can standup existing components or APIs behind elastic microserivces
[Cloud Scaling](cloudscaling.md) | Take existing APIs and push them into the cloud to increase ability to withstand copmpute spikes in a growing system
[AppStatus](#scaling.md) | Bolt real-time monitoring through a Cloud console onto the side of an existing app or service without refactoring