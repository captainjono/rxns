# Recording and Playback

Event sorucing faciliates creating Apps that consicesly express each state transition through the broacasting of an event. By persistanting this event stream you can unlock a varity of scenarios that boost your productivity, i*ncluding*:

+ `Unit testing` | Playing back the same stream of events through the same functions to validate that the system is consistant
  
+ `Oboarding experiences` | In the same way you express state transitions with events, you can express user input as events that orchestration services react to to trigger App actions. This effectively lets you automate you UI and this process can be used in-App to create guided tours of features and other productivity boosters you thought you didnt have time to implement.

RxnsAPI

* `ITapeRepository` | Used to store tapes
    * FIleSystemTapeRepository | Store tapes on disk
    * InMemoryTapeReportisity | Store tapes in memory for the duration of the Apps execution  * 
* `ITapeStuff` | The model for the tape
* `ITapeArray` | A place where you can access a set of tapes efficently in parrallel
* `ITapeArrayFactory` | A component which creates tape arrays
* `ITapeRecorder`
  * `UserAutomationPlayer` | A specialised recorder which records and plays back tapes, allowing you to map User Actions to each event.
* `IRxnTapeToTenantRepoPlaybackAutomator`
  * `RxnToTenantModelPlaybackAutomator` | Plays back tapes into a `ITenantModelRepositry`

