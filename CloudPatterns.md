# DDD

Reactions encourges you to develop applications with a layering inspired by Domain driven design.

These patterns are filed under the `Rxns.DDD` namespace

Reactions | Usage
-|-
Domain Querys / Commmands | Impement decoupled App architecture where Domain Services respond to questions in a cloud scalable manner via a ICommandService that implements the actual communication layer. This layer can be accessed locally or remotely via a RESTful API and implements the `mediator` pattern which allows you to create `Pre/Post Cmd/Qry` handlers
DomainEvents | Define state transitions in you buisness logic and react to these events with
ITenantModelFactory/Repository<T> | Store your state transitions as contiguious event streams for later playback, auditing etc
AggRoot | The logical root entity in your domain model where state transitions are triggered from

