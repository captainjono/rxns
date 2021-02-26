# Domain Contexts

Provide a central way to access the different domain concerns of your App. They are conviently located and easily created to handle the various command and query concerns of your reactive App

These contexts faciliate the following:
- Consistant API usage:
- Optimise hot paths in your App in a wholeistic way
- Create domain APIs that allow users to quickly discover the recommended pathways through your API without them need to read extra docuemntation
- Allow you to move your services around without needing to change your domain models API. You service can be local to the app or over the wire, the domain context allows you to use the API as if it was local.

All these techniques can be applied incorrectly to it is recommended that you always be vigilant to gauard against pollution of your domain context. The following considerations should be taken into account when designing the layout and composition of your domain context
1. Its speed requirements: 
-    How many times per second does it get accessed?
-    What kind of overhead does it take to create a context from the params of an event?
2. Choosing what to put on the context:
-    What kind of operations does the domain context facilitate. How will that API sprawl over time?
     -    You will often split up the context as it grows into more conseise sections that are easier to manifest.



```c#
    public interface ITenantContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
        ITenantContext GetContext(string tenant);
    }
```
this context allow you to convert a username or a tenant into powerful API that you can manipulate user data with and perform other complex system actions.
-It is very quick to create because it limits the API surface and only provides access to operations in a lazy fashion because it was determined that this suited the usage patterns of the system.


here is an implementation of that tenant context

```c#
    public class TenantContext : ITenantContext
    {
        public string Tenant { get; private set; }
        public Lazy<TenantSettings> Cfg { get; private set; }
        
        public IObservable<Alerts> Alerts { get; private set; }
        public ITenantDatabaseFactory DatabaseContext { get; private set; }
        public ITenantDiscardRepository DiscardContext { get; private set; }

        public IDictionary<string, Document> Documents { get; private set; }
        public MyOrganisation Org { get; private set; }

        public TenantContext(
            string tenant, 
            ITenantDatabaseFactory dbFactory,
            ICurrentDocumentsService documents, 
            ICurrentTenantsService currenTenants, 
            ITenantDiscardRepository DiscardRepo, 
            Func<string, ILookupConfigurationProvider> cfgProvider
            )
        {
            Tenant = tenant;
            DatabaseContext = dbFactory;
            Documents = documents.CurrentDocs(tenant);
            Alerts = currenTenants.LookupAlertsFor(tenant);
            DiscardContext = DiscardRepo;
            Cfg = new Lazy<TenantSettings>(() => new TenantSettings(cfgProvider(tenant)));
        }
    }
```

this is the user context factor which spawns a user context either based on the current threads princicple or if that doesnt exist, a lookup to a legacy userDb table that is still part of a monolith db store

```c#
    public class UserContextFactory : IUserContextFactory
    {
        private readonly ITenantDatabaseFactory _dbFactory;

        public UserContextFactory(ITenantDatabaseFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public IUserContext GetUserContext(string tenant, string userName = null)
        {
            return Thread.CurrentPrincipal != null 
                    && (Thread.CurrentPrincipal.Identity.Name == userName 
                    || userName.IsNullOrWhitespace())
                    ? 
                    new ThreadBasedRvUserContext() : 
                    new LegacyDbUserContext(
                        tenant, 
                        userName, 
                        _dbFactory.GetUsersContext(_dbFactory.GetContext(tenant))
                        );
            }
    }
```
- You can see here how are user contexts now stack together and create powerful API flows with adding limited overhead to every request that may only use 1/10th of the entire network of contexts total capapabilities