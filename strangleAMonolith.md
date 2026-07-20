# Strangle The Monolith

Often event sourcing can fit a need when it comes to reducing resource load on existing systems and components. Rxns provides a serious of components, patterns and practices which can help you to add event sourcing in a unobtrusive, peicemeal way to an existing code-base.


Pattern | Application
-|-
[RxnDecorator](rxndecorators.md) | Add event sourcing to existing APIs / objects without recompiling them
[ViewProcessor](ViewProcessors.md) | Deprecate databases / tables / legacy systems with strangler DatabaseViewProcessors. Or add caching infront of existing data stores or integrations which are struggling to scale with the rest of your system
[Reactor](reactors.md) | Create a seperate enclave where you can standup existing components or APIs behind elastic microserivces
[Cloud Scaling](cloudscaling.md) | Take existing APIs and push them into the cloud to increase ability to withstand copmpute spikes in a growing system
[AppStatus](#scaling.md) | Bolt real-time monitoring through a Cloud console onto the side of an existing app or service without refactoring


This is an example of [domain command handler](dddaggs.md) which transitions a db centric legacy system to an event source micro-app. 
-   Here fields in a legacy monolith database have been profiled and selected intelligently based on the cloud providers desired plan specifics in order to reduce the overhead of certain hot fields and reduce db teirs.
-   It uses a set of [domain contexts](dddcontexts.md) to ease the transition of converting events to legacy calls ato the logged in user and correct tenant db. You can then manage these concerns at other levels in wholistic ways that [are cloud durable](reliability.md)
-   The example uses an [event sourced aggregate](#) as the microservice persistant mechanism to add highly scalable comment'ing features to an existing CRM style system

```c#
        public IObservable<DomainCommandResult<long>> Handle(AttachMessageToDocumentCmd cmd)
        {
            lock (GetDocmentLock(cmd.Tenant, cmd.FileNumber))
            {
                return Rxn.Create(() =>
                {
                    //make sure your LookupId functions hit caches to reduce db chatter
                    //otherwise your new Microservice will cause erratic spikes in its usage.
                    //also ensure you have sorted transient failures otherwise you can 
                    //corrupt the state of the write model                        
                    return ReliablyRun(cmd.Tenant, lookup => lookup.Run(db =>
                    {
                        long id;

                        
                        //use the event as the mechanism to derive user/tenant context for database operations
                        var userContext = _exeContext.FromUserDomain(cmd).User.Value;
                        var enteredBy = "{0} {1}".FormatWith(userContext.Name.FirstName, userContext.Name.LastName).Trim();
                        db.rri_DocumentComment( //use legacy stored proc to persist data into read model
                            _legacyDb.LookupUserId(cmd.Tenant, cmd.UserName), 
                            _legacyDb.LookupDocumentId(cmd.Tenant, cmd.DocumentNumber), 
                            cmd.Subject, 
                            cmd.Message, 
                            enteredBy, 
                            userContext.IsPrivate, 
                            cmd.Created, 
                            out id
                            );

                        //return an event sourced message which can be used to update 
                        //redundant write models or webcache or data lakes etc
                        var sideEffect = new NewDocumentMessageAdded(
                                                                cmd.FileNumber, 
                                                                cmd.UserName, 
                                                                id.ToString(), 
                                                                cmd.Subject, 
                                                                cmd.Message, 
                                                                enteredBy, 
                                                                cmd.IsPrivate, 
                                                                userContext.IsPrivate, 
                                                                cmd.Created
                                                                );
                        sideEffect.AssignTenant(cmd.Tenant);

                        return DomainCommandResult<long>.FromSuccessfulResult(id, sideEffect);
                    }));
                });
            }
        }
```

your future state in a pure event source model would look like this. It uses an `ITenantModelRepository<>` to store the aggregate with an [ITapeArray](playback.md) and return a response in `< 10ms`, with a side-effect event generated which could be utimately used with a `AzurePushClient` to [send notifications to users](rxnInAzure.md), or could be [lazily persist that to a cloud db with a `sharding queue processor` or fan it out via a [RxnManager](buildingblocks.md)] backed by a [AzureQueueBackingChannel](rxnInAzure.md) to power cloud functions that could update a [read model such as a legacy db with a db view processor pattern](ViewProcessors.md)

```c#
        public IObservable<DomainCommandResult<bool>> Handle(RevokeDocumentAccessForIndividualCmd cmd)
        {
            lock (GetDocumentLock(cmd.Tenant, cmd.FileNumber))
            {
                return Rxn.Create(() =>
                {
                    if (cmd.Tenant.IsNullOrWhitespace()) 
                        return false.AsFailureWith(new DomainCommandException(cmd, 
                                                        "No tenant supplied for command:id {0}", cmd.Id));
                    if (cmd.DocumentNumber.IsNullOrWhitespace()) 
                        return false.AsFailureWith(new DomainCommandException(cmd, 
                                                        "No DocumentNumber supplied for command:id {0}", cmd.Id));

                    var matter = _matterRepo.GetById(cmd.Tenant, cmd.FileNumber);
                    matter.AssignFileNumber(cmd.FileNumber);
                    matter.RevokeExternalAccessForIndividual(cmd.FirstName, cmd.LastName);
                    var processed = _matterRepo.Save(cmd.Tenant, matter).ToArray();

                    return true.AsSuccessWith(processed);
                },
                error => false.AsFailureWith(error));
            }
        }
```