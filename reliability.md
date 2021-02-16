# ReliabilityManager
  All RxnApps operations should be funneled through the reliability manager. This does mean it becomes a single point of failure, rather, it acts as quality gateway to make your apps operations consistant and durable.  
  - This pattern allows you to seperate the transport mechanism from the actual data you wish to transfer
  - These patterns allow you to implement consistant relability schemantics without the users of the API every reall knowing. This is an advantage in large teams or scenarios when the codebase is worked on by many people of differing Domain expertise. 
-Coordinates the always on nature of a RxnsApp, and makes it resilliant to cloud native conditions such as transiant failures in connection durability.
-This feature is implemented using cut down version of `Polly`


types of reliabiliy services
Each reliability operation can be configured with its own RetryPolicy that cator for occasionally cloud environments
  - Retry policies:
    - Exponential backoff
    - Linaear backoff
    - Circut breaker
    - Fallback operations
  - Types of standard RxnApp services
      -  `CallOverHttpForever(httpClient => {})` : provides a reliable http connection to the caller so they only need to care about what they are sending, not how. 
      -  `CallDatabase(sqlClient => {})`: provides a reliable database connection to the consumer which cators for standard error / retry scenarios.
    - 
  - CallDatabase() 
  - THis interface abstracts away the authoriations, location and 
