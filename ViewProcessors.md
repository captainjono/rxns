# ViewProcessors

These are a specialised type of *event-driven* Orhcestrator that materialises event streams into one or more durable `views` that will be updated in real-time to reflect the current state of the system. These views are basically precomputed data-sets that are tailoed to an APIs structure, either to optimise UX, transport times, bandwidth usage, or costs or other needs spceific to your use-case


In short: A view is *simply* a representation of your data *that makes sense to your domain*. 

- Types of views
    - **Database***ViewProcessor* | Used to materilise event streams into any type of database external to your App (SQL, NoSql, KVStore etc)    
    - **Cacheing***ViewProcessor* | Materilise event streams into fast volotile transient storage optimised for a DomainAPIs response format
    Uses:
        + Buffer read or write operations to a `Database`
        + Buffer read or write operations to a `DomainAPI`
    - **Batching***ViewProcessor* | These types of views will perform operations on its target in an *intelligent*, domain specific way, to improve bandwidth
    or response times of the views
    Types:
        

## Patterns & Traits

To be reliable in transient, occasionally connected or high-scale environments; you need to you need to build reliablity features into them in a consistant way to reduce tech debt.

* `Posion letters queues` or `Disard Buckets` | When processing event streams, one must be weary of malformed packets that can lead to catatastphies like crashes and downtime. If an event causes an exception in your App, and this event is user generated, it can lead to DoS sitution very quickly.  These types of events are reffered to as `Point letters`. You must deal with them.  
  + `Retry bucket` | This is a seperate container to place these messages while you work out what to do with them. At first, this process can be manually performed, but as your app grows, you need automated stratergies to deal with these situations before they get out of hand. 

* `Use of reliability manager` | All database calls are funneled through the reliability subsystem which supports the poision letter queues. The `CallDatabase` function handles common database errors appropriotely through policies which are defined globally to the app.

* `Encapsulated boilerplate patterns` | The `Process` methods call out to various `Lookup` functions like  `GetOrCreateName(..)` and `CreateStaffRelationshipWithDocument(..)` to collect data and organise it for batch insertion into the database when possible. They delegate to the `ReliabilyRun(() => ...)` method to handle errors while performing these operations that should only be processed in an at most-once fashion.

* Reports to `AppStatus` | Monitoring your view processors for *undiscovered* `poison letters` situations is only one reason why real-time monitoring is critical to the success of your view processor.

* Intelligent `caching` | Caching is hard. Because its highly dependant on circumstance. Here, as we have written the *viewprocessor* we have noticed certain operations done over and over again, because we have written a well-reasoned domainAPI and our code editor make it obvious by proclaiming `35 references` to `GetUserById()` as we scroll our mouse over it. We have combined knowledge with our understanding of the cloud database performance charactoristics for the teir we are subscribed too, and have deiced that cachcing then debouncing the call for `30mins` is *sensible*. We have then repeated the process, potentially using [Event stream playback](#playback) to make a repeatable integration test that we can base further decisons off.

* `Unit tested` | All your orchestration functions should have test cases. This way you know what will fail before it happens and reduce your changes of poision messages being produced, as well as validating they populate the DatabaseAPI in complience with the expectations.

* `Consistant code` | This is different from *linting*. 
  * Considered use of variable names and spacing to group like operations, provide steps and reduce cognative load 
  * Limited use of inline functions which can distract from reading the steps of a domain operation. 

>NOTE: In error situations your services should return error codes instead of throwing. Dont throw errors. They can cause DoS attacks against your system.

## Scaling considerations

* Minimise `API chatter`. The aim of your orchestration service is to coordinate the API-API transaction, not bottle-neck it. Usually though, when it comes to databases, you will likely hit limits on the database end way before you hit limits in the orchestration layer.
* Guidance:
  * Before you call the `DatabaseAPI`, gather all the information you need
  * If you are importing data or otherwise doing write-heavy things often; work out how you can batch writes so you can send a series of writes per connect lifecycle.
  * In last-write-wins scenarios consider the use of the `LazyCacheDecortor` which can debounce writes to a `DatabaseAPI` to significantly *reduce* `Compute` as well

* Consider the use of `ShardingQueueProcessors` in multi-tenanted environments to give each tenant predictable load even in cases where one tenant is signifantly demanding more resources then another

* [Backpressure](#backpressure) should be monitored to detect inbalance in consumer / procuder relationship. Monitor your view with `AppStatus`, make small, informed changes to your `scaleout stratergy` only in reaction to real-world data. ie. if you view is becoming bottlenecked by compute, you can `Cloud scaleout` to and run your view in a `Cloud Logic App` that a cloud provider will scale for you when backpressure is detected.

  
# RxnsAPI
*Recommendated approachs*

Example are for a database which associated users and documents together with some security featuress. The concepts can be adapted to other integration use-cases, not just with relation DBs.

The ViewProcessor -

```c#

public class DatabaseViewProcessor : ReportsStatus, //logging to appStatus portal
                                    IRxnProcessor<IDomainEvent> //base-interfaces are great for deprecating database while implementing CQRS. Specific interfaces are good at removing spike from a database utilisation graph
                                    IReactionCfg // attach this service to a specific reactor to isolate it
{ 
        private readonly IExecutionContextFactory _execFactory; //all operations done via a context 
        private readonly RetryBucket<ITenantDomainEvent> _retryBucket; // handle poison letters
        private readonly IExpiringCache<string, object> _idCache; //buffer common operations required to looking keys etc for db

        private static readonly IScheduler _singleThread = new EventLoopScheduler(); //single threaded to make it easier to reason about
        public IObservable<IEvent> ConfigureInput(IObservable<IEvent> pipeline)
        {
            return pipeline.ObserveOn(_singleThread).SubscribeOn(_singleThread);
        }

        public string Reactor => "MonolithStragler"
        public IDeliveryScheme<IEvent> InputDeliveryScheme { get; private set; }
        public bool MonitorHealth { get { return true; } }

        //since this is an event processor, it should get a new thread to begin on, and maintain that same thread for the apps duration
        //having a single writer is much less complex then a multi-writer approach. Keep your domain small, or design sharding into the db schema
        public DatabaseViewProcessor(ITenantDatabaseFactory contextFactory, IExecutionContextFactory execFactory, IReliabilityManager reliably, IScheduler retryScheduler = null)
        {
            _execFactory = execFactory;
            _retryBucket = new RetryBucket<ITenantDomainEvent>(e => { Process(e).WaitR(); }, //call the view process method
                                                                (evt, e) => OnError(e, "Retrying failed for {0}", evt.GetType().Name), //capture error
                                                                (evt, error) => MarkAsPoison(evt, error), //when retry fails
                                                                "MyDatabaseView",
                                                                retryScheduler: retryScheduler ?? RxnApp.TaskPool, //retry on this scehduler
                                                                retryIn: TimeSpan.FromMinutes(30)); //wait long periods for transiat errors like database scheme deployment issues

            _idCache = ExpiringCache.CreateConcurrent<string, object>(TimeSpan.FromMinutes(20),  retryScheduler);
        }

        private void MarkAsPoison(IDomainEvent @event, Exception error)
        {
            _execFactory.FromDomain(@event).Tenant.Value.DiscardContext.DiscardPoisonEvent(@event, error).WaitR();
        }

        private readonly Action<IDomainEvent, Exception, RetryBucket<ITDomainEvent>, IReportStatus> _retryIf = (@evt, e, retryBucket, reporter) =>
        {
            if (e is NullReferenceException || e is ArgumentNullException)
                reporter.OnError(e, "Bad event received: {0}", @evt.ToJson().Replace('{', '[').Replace('{', ']'));
            else if (e is RuntimeBinderException)
                reporter.OnError(e, "Event not handled yet: {0}", @evt.GetType());
            else
            {
                //unexpected error, retry later
                reporter.OnError(e, "Processing failed for {0} -> {1}", evt.GetType().Name, e.Message);
                retryBucket.Add(@evt);
            }
        };


           /// <summary>
        /// Note:
        /// We want to do things here in a sync way to maintain @event ordering
        /// so we enforce strict no-async with a poorly implemented sequence
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public IObservable<IEvent> Process(ITenantDomainEvent @event)
        {
            try
            {
                if (_execFactory.FromDomain(@event).Tenant.Value.SourceSystem.WaitR() != SourceSystems.Import)
                    return Observable.Empty<IEvent>();

                Apply((dynamic)@event);
                    return Observable.Empty<IEvent>();
            }
            catch (Exception e)
            {
                _retryIf(@event, e, _retryBucket, this);
                return Observable.Empty<IEvent>();
            }
        }

        #region Staff Aggregate processing

        public int GetStaffId(string staffCode, IDbConnection r)
        {
            var staff = r.FirstOrDefault<Staff>(w => w.StaffCode == staffCode);
            if (staff == null) throw new StaffNotFoundException(staffCode);

            return staff.Id;
        }

        public Name GetNameByStaffId(int staffId, IDbConnection r)
        {
            return r.FirstOrDefault<Name>(w => w.Id == staffId);
        }


        public void Apply(StaffCreatedEvent @event)
        {
            ReliablyRun(@event.Tenant, batch =>
            {
                batch.Run(r => r.Insert(new Staff() { StaffCode = @event.StaffCode, IsActive = true }));
            });
        }

        public void Apply(StaffRoleAssignedEvent @event)
        {
            ReliablyRun(@event.Tenant, batch =>
            {
                var staffId = GetStaffId(@event.StaffCode, batch.Connection);

                var roleId = batch.Run(r =>
                {                    
                    var existing = r.FirstOrDefault<StaffRole>(s => s.Description == @event.Description);
                    if (existing != null) return existing.Id;

                    existing = new StaffRole() { Description = @event.Description };
                    r.Insert(existing);
                    existing.Id = (int)r.GetLastInsertId();

                    return existing.Id;
                });

                batch.Run(r => r.Update<Staff>(new { StaffRoleID = roleId }, w => w.Id == staffId));
            });
        }

        public void Apply(StaffBirthdayAssignedEvent @event)
        {
            ReliablyRun(@event.Tenant, batch =>
            {
                var staffId = GetStaffId(@event.StaffCode, batch.Connection);
                var name = GetNameByStaffId(staffId, batch.Connection);

                AssignBirthdayToIndividual(name.FirstName, name.LastName, @event.DateOfBirth, batch);
            });
        }

        public void Apply(StaffPhoneNumberChanged @event)
        {
            ReliablyRun(@event.Tenant, batch =>
            {
                var staffId = GetStaffId(@event.StaffCode, batch.Connection);
                var name = GetNameByStaffId(staffId, batch.Connection);

                AssignPhoneToName(name.FirstName, name.LastName, @event.Phone.Type, @event.Phone, batch);
            });
        }

        public Name GetOrCreateName(string firstName, string lastName, IDbConnection r, bool isOrganisation = false)
        {
            return (Name)_idCache.GetOrLookup("{0}$n${1}_{2}".FormatWith(r.Database, firstName, lastName),
                _ =>
                {
                    var name = r.FirstOrDefault<Name>(w => w.FirstName == firstName && w.LastName == lastName && w.IsOrganisation == isOrganisation);
                    if (name != null) return name;

                    name = new Name() { FirstName = firstName, LastName = lastName, IsOrganisation = isOrganisation };
                    r.Insert(name);
                    name.Id = (int)r.GetLastInsertId();

                    return name;
                }).Wait();
        }


        public NameEmail AssignEmailToName(string firstName, string lastName, string email, string emailType, IOrmBatchContext batch)
        {
            var name = batch.Run(r => GetOrCreateName(firstName, lastName, r));

            var emailAddressType = batch.Run(r =>
            {
                var type = r.FirstOrDefault<EmailType>(w => w.Description == emailType);
                if (type != null) return type;

                type = new EmailType() { Description = emailType };
                r.Insert(type);
                type.Id = (int)r.GetLastInsertId();

                return type;
            });

            var emailAddress = batch.Run(r =>
            {
                var type = r.FirstOrDefault<Email>(w => w.EmailAddress == email);
                if (type != null) return type;

                type = new Email() { EmailAddress = email };
                r.Insert(type);
                type.Id = (int)r.GetLastInsertId();

                return type;
            });

            var nameEmail = batch.Run(r =>
            {
                var existing = r.FirstOrDefault<NameEmail>(w => w.EmailTypeID == emailAddressType.Id && w.EmailID == emailAddress.Id && w.NameID == name.Id);
                if (existing != null) return existing;

                existing = new NameEmail
                {
                    EmailID = emailAddress.Id,
                    EmailTypeID = emailAddressType.Id,
                    NameID = name.Id
                };
                r.Insert(existing);
                existing.Id = (int)r.GetLastInsertId();

                return existing;
            });

            return nameEmail;
        }

        public void CreateStaffRelationshipWithDocument(string tenant, string documentIdentifier, string staffCode, string category, byte sequence)
        {
            ReliablyRun(tenant, r =>
            {
                var staffId = GetStaffId(staffCode, r.Connection);
                var name = GetNameByStaffId(staffId, r.Connection);
                var individual = new IndividualAssociatedWithDocumentEvent(documentIdentifier, category, category, name.FirstName, name.LastName, sequence);
                individual.ForTenant(tenant);
                Apply(individual);

                var changes = new IndividualRelationshipAssociationUpdatedEvent(documentIdentifier, category, category, name.FirstName, name.LastName);
                changes.ForTenant(tenant);
                changes.Added<DocumentRelationship, float?>(s => s.SortOrder, sequence);
                changes.Added<DocumentRelationship, byte>(s => s.RelationshipRole, (byte)RelationshipRoleType.Staff);
                Apply(changes);
            });
        }

}
```

BatchingViewProcessor -


```c#
 public class BatchingViewProcessor
 {
    private readonly ITenantDatabaseFactory _pool;
    protected readonly IReliabilityManager _reliably;
    private readonly IScheduler _dbScheduler;

    public BatchingViewProcessor(ITenantDatabaseFactory pool, IReliabilityManager reliably, IScheduler dbScheduler = null)
    {
        _pool = pool;
        _reliably = reliably;
        _dbScheduler = dbScheduler;
        _dbScheduler = dbScheduler ?? RrSchedulers.Immediate;
    }

    /// <summary>
    /// For times when you want to maintain a single transaction for an entire sequence
    /// of calls to the database. Will retry on transient errors
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="tenant"></param>
    /// <param name="ormTask"></param>
    /// <returns></returns>
    protected virtual T ReliablyRun<T>(string tenant, Func<IOrmBatchContext, T> ormTask)
    {
        var batch = _pool.GetContext(tenant).StartBatch();

        using (var trans = batch.Connection.BeginTransaction())
        {
#if DEBUG
            var timer = new Stopwatch();
            timer.Start();
#endif
            var result = _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
            trans.Commit();
#if DEBUG
            timer.Stop();
            Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
            return result;
        }
    }

    /// <summary>
    /// For times when you want to maintain a single transaction for an entire sequence
    /// of calls to the database. Will retry on transient errors
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="ormTask"></param>
    protected virtual void ReliablyRun(string tenant, Action<IOrmBatchContext> ormTask)
    {
        var batch = _pool.GetContext(tenant).StartBatch();

        using (var trans = batch.Connection.BeginTransaction())
        {
#if DEBUG
            var timer = new Stopwatch();
            timer.Start();
#endif
            _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
            trans.Commit();
#if DEBUG
            timer.Stop();
            Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
        }
    }

    /// <summary>
    /// For times when you want performance and are not mutating database records
    /// </summary>
    /// <param name="tenant"></param>
    /// <param name="ormTask"></param>
    protected virtual void ReliablyLookup(string tenant, Action<IOrmContext> ormTask)
    {
        var batch = _pool.GetContext(tenant);

#if DEBUG
        var timer = new Stopwatch();
        timer.Start();
#endif
        _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
#if DEBUG
        timer.Stop();
        Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
    }

    /// <summary>
    /// For times when you want performance and are not mutating database records
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="tenant"></param>
    /// <param name="ormTask"></param>
    /// <returns></returns>
    protected virtual T ReliablyLookup<T>(string tenant, Func<IOrmContext, T> ormTask)
    {
        var batch = _pool.GetContext(tenant);

#if DEBUG
        var timer = new Stopwatch();
        timer.Start();
#endif
        var result = _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
#if DEBUG
        timer.Stop();
        Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
        return result;
    }
    }
}
```


Recommend Unit test granularity:
```c#
    public void should_create_and_delete_document()
    {
        var fileNumber = Guid.NewGuid().ToString();

        //create document
        var document = new DocumentCreatedEvent(tenant, fileNumber);
        sut.Process(document).WaitR();

        db.Run(r => r.FirstOrDefault<RvpDocumentOnlyFileNumber>(w => w.FileNumber == fileNumber)).Should().NotBeNull("the document should be created");

        //then delete it
        var documentDeleted = new DocumentDeletedEvent(tenant, fileNumber);
        sut.Process(documentDeleted).WaitR();

        //then verify its deleted
        db.Run(r => r.FirstOrDefault<RvpDocumentOnlyFileNumber>(w => w.FileNumber == fileNumber)).Should().BeNull("the document should be deleted");
    }

```

A poision message test:

```c#
        [Test]
        [TestCase(1, ExpectedException = typeof(ReceivedCallsException))]
        [TestCase(5)]
        [TestCase(3)]
        public void should_discard_poison_events(int retryAttempts)
        {
            var testScheduler = new TestScheduler();
            var poison = new PoisonedEvent(null, "type", "state");

            executionContext.currentTenants.LookupSourceSystemFor(Arg.Any<string>()).Returns(_ => Rxn.Throw<SourceSystems>(new Exception())); //trigger the exception

            executionContext.db = Substitute.For<IOrmTransactionContext>();
            executionContext.db.When(c => c.StartBatch()).Do(_ => { throw new Exception("test error"); }); //got up to here while working on this test

            container.Dispose();
            sut = new DatabaseProcessor(executionContext.databaseFactory, executionContext.Context, IntegrationTestFactory.GetReliablityManagerDisabled(), testScheduler);
            sut.ReportToConsole().DisposedWith(this);

            var input = new Subject<IEvent>();
            input.OfType<IDomainEvent>().SelectMany(e => sut.Process(e)).ToArray().Until(e => Assert.Fail(e.Message));

            input.OnNext(poison);

            for (int i = 0; i < retryAttempts; i++)
            {
                Console.WriteLine("Retry {0}", i);
                testScheduler.AdvanceBy(TimeSpan.FromMinutes(30).Ticks + 1);
            }

            executionContext.discardRepo.Received(1).DiscardPoisonEvent(poison, Arg.Any<Exception>());
        }
```

Always develop your views with a Performance test so you can measure before you optimise:
```c#

    [Test]
    public void should_load_all_documents_with_efficent_use_of_memory_and_CPU()
    {
        var documentLookup = new Dictionary<string, RvsDocument>();
        var documentRepo = container.Container.Resolve<ITenantModelRepository<RvsDocument>>();
        var resources = new SystemResourceService(new WindowsSystemServices());
        var memory = resources.GetMemoryUtilisation();

        Console.WriteLine("Starting download");
        var start = DateTime.Now;
        var allDocuments = db.Run(r => r.Select<RvpDocumentOnlyFileNumber>());
        foreach (var m in allDocuments)
        {
            var s = DateTime.Now;
            documentLookup.Add(m.FileNumber, documentRepo.GetById(tenant, m.FileNumber));
            Console.WriteLine("FileNumber {0}/{1}  => {2}", m.FileNumber, DateTime.Now - s, (resources.GetMemoryUtilisation() - memory).ToFileSize());
        }
        Console.WriteLine("Total time: {0}/{1} => {2}", allDocuments.Count, DateTime.Now - start, (resources.GetMemoryUtilisation() - memory).ToFileSize());            
    }


```

Dont forget the integration test

This test is for a Database centric pipeline that has had work offloaded to an DDD Aggregate that is now Event Sourced with a `EventSourcingTenantModelRepository`. This aggregate is now the source of truth, and it can be written to in `~8ms` compared to the database which is `200-500ms`. The events are then streams to the ViewProcessor and this test ensures that for the root aggregate, this process works.

```c#
        [Test]
        public void should_support_document_individuals_workflow()
        {            
            var documentIdentifier = Guid.NewGuid().ToStringMax(5);
            var category = Guid.NewGuid().ToStringMax(5);

            //clean the db so its ready for the test
            db.Run(r => r.DeleteAll<Name>());
            db.Run(r => r.DeleteAll<Email>());
            db.Run(r => r.DeleteAll<EmailType>());
            db.Run(r => r.DeleteAll<Document>());
            db.Run(r => r.DeleteAll<DocumentRelationship>());
            db.Run(r => r.DeleteAll<RelationshipCategory>());
            db.Run(r => r.DeleteAll<DocumentName>());

            //create the DDD aggregate & mutate it to generate events we will then push into the database
            var document = new Document(tenant, documentIdentifier);
            document.CreateRelationship(new IndividualRelationship(documentIdentifier, category, category, "mike", "jack"));
            var updatedDocument = document.MakeDeepCopy();
            var individual = updatedDocument.Individuals[0].Individual;
            individual.GiveName(new Name("mary", "scotts"));
            individual.AssignAnEmail("home", "test@test.com");

            updatedDocument.GrantIndividualExternalAccess("test", "web", "test@test1.com");
            updatedDocument.GrantIndividualExternalAccess("test1", "web1", "test1@test1.com");
            updatedDocument.GrantIndividualExternalAccess("test2", "web2", "test2@test1.com");
            updatedDocument.RevokeExternalAccessForIndividual("test1", "web1");
            document.SyncWith(updatedDocument, syncService);

            //get all the pending changes and then push them into the database
            foreach (var e in document.GetUncommittedChanges())
            {
                Console.WriteLine(e.GetType());
                ((IObservable<IEvent>)sut.Process((dynamic)e)).WaitR(); 
            }

            //ensure only 1 name exists per category. 
            var names = db.Run(r => r.Select<Name>());
            names.Length().Should().Be(4, "there should be a valid name, 2 orphanded records, and 2 test web user");
            db.Run(r => r.Select<DocumentRelationship>()).Any(a => a.Description == "test1 web1").Should().BeFalse("access should be removed for test1"); //verify remove bug where access is revoked but doesnt properly match the correct user
            db.Run(r => r.Select<DocumentRelationship>()).Should().Contain(c => c.Description == "mary scotts", "the category should be set").And.HaveCount(3, "1 individual and 2 web user relationships should exist");
            db.Run(r => r.Select<DocumentRelationship>()).Any(a => a.Description == "test2 web2").Should().BeTrue("access should not be removed for test2");
            names.Last().FirstName.Should().Be("test2", "the firstname should be updated");
            names.Last().LastName.Should().Be("web2", "the lastname should be updated");

            db.Run(r => r.Select<Document>()).Should().ContainSingle(m => m.FileNumber == documentIdentifier, "the filenumber should be attached");
            db.Run(r => r.Select<RelationshipCategory>()).Should().Contain(w => w.Description == category, "only 1 relationshipCategory should exist").And.HaveCount(2, "2 relationship categorys should exist - one webuser. one other");
            db.Run(r => r.Select<DocumentName>()).Should().ContainSingle(n => n.NameID == names.Last().Id, "the document should reference the name");
            db.Run(r => r.Select<Email>()).Should().ContainSingle(e => e.EmailAddress == "test@test.com", "the email should be assigned");
            document.MarkChangesAsCommitted(document.GetUncommittedChanges());

            //change email to ensure old email is removed
            updatedDocument.GrantIndividualExternalAccess("test1", "web1", "test1@test1.com");
            individual.AssignAnEmail("home", "cool@dude.com");
            document.GrantIndividualExternalAccess("hacky", "hack", "hack@needto");
            document.SyncWith(updatedDocument, syncService);

            document.Individuals.Any(i => i.Individual.Name.FirstName == "hacky").Should().BeTrue("the web user category should not sync because its maintained by another subsystem");

            //verify readModel
            foreach (var e in document.GetUncommittedChanges())
            {
                Console.WriteLine(e.GetType());
                ((IObservable<IEvent>)sut.Process((dynamic)e)).WaitR();
            }

            db.Run(r => r.Select<Email>()).Should().ContainSingle(e => e.EmailAddress == "cool@dude.com", "the email should be updated");
            db.Run(r => r.Select<EmailType>()).Should().ContainSingle(e => e.Description == "home", "there should only be one emailtype record");
            db.Run(r => r.Select<DocumentRelationship>()).Should().Contain(c => c.Description == "mary scotts", "the category should be set").And.HaveCount(5, "1 individual and 4 web user relationships should exist");
            db.Run(r => r.Select<DocumentRelationship>()).Any(a => a.Description == "test1 web1").Should().BeTrue("access should be granted for test1");
        }
```
