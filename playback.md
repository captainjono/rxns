# Recording and Playback

Event sorucing faciliates creating Apps that consicesly express each state transition through the broacasting of an event. By persistanting this event stream you can unlock a varity of scenarios that boost your productivity, i*ncluding*:

+ `Unit testing` | Playing back the same stream of events through the same functions to validate that the system is consistant
  
+ `Oboarding experiences` | In the same way you express state transitions with events, you can express user input as events that orchestration services react to to trigger App actions. This effectively lets you automate you UI and this process can be used in-App to create guided tours of features and other productivity boosters you thought you didnt have time to implement.

+ `Highly Scalable cloud infrastructure` | Tape arrays that persist event sourced aggrgates can be used as a replacement for legacy database tables where data structures are favourable to the contraints of an event sourced data layer
  + Data should generally be small, otherwise use of reference storage pattern is required to augment the event source domain model which create extra complexity in system maintence.

## Reference data storage pattern

Since event driven systems deal with sending and receiving millions of event per/second, there will always be constraints about exactly what can be stored in an event.

Data that is suitable for events will display the following charactoristics
  * is < `100kb` when serilised over a bus
  * is bounded in size

In cases where most of yout data fits this general pattern but you have outliers such as the requirement to associate `blob style data` *with* **events**, you should *use* the following pattern

> You will likely want to wrap these in some sort of transaction logic
1. Use a `IReferenceTEnantModelRepositortyFactory<>` to create a reference store.
2. Add the blob to the reference store and
3. On the event, `Add` a `ReferenceId` property that will be used to store the `foreign key` for the `blob data` you wish to associate with the event
4. Store the event as usual in an aggregrate, but this time also specifcy the `ReferenceId` of the blob data just stored, as you create the event

Example 
```C#


```

### Things to note

This pattern is not without its risks. This coupling of an event to a reference store creates overhead and also constraints. Be warey of how your implementation may effect the following areas:
  * Playback of events, orphaned reference data
  * The complexity of moving event data around with refrence data
  * 

# Rxns DomainAPI reference

>* `ITapeRepository` | Used to store tapes
>   * FIleSystemTapeRepository | Store tapes on disk
>   * InMemoryTapeReportisity | Store tapes in memory for the duration of the Apps execution  * 
>* `ITapeStuff` | The model for the tape
>* `ITapeArray` | A place where you can access a set of tapes efficently in parrallel
>* `ITapeArrayFactory` | A component which creates tape arrays
>* `ITapeRecorder`
 >   * `UserAutomationPlayer` | A specialised recorder which records and plays back tapes, allowing you to map User Actions to each event.
>* `IRxnTapeToTenantRepoPlaybackAutomator`
>  * `RxnToTenantModelPlaybackAutomator` | Plays back tapes into a `ITenantModelRepositry`

