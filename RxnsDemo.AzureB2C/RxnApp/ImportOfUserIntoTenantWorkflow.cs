using System;
using System.Collections.Generic;
using Rxns.CQRS;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.RxnApps.Events
{
    public class ProgressOfUserImportIntoTenantQry : TenantQry<ProgressOfImport>
    {
        public string ImportId { get; set; }

        /// <summary>
        /// Only to be used by serilisers
        /// </summary>
        public ProgressOfUserImportIntoTenantQry() : base(null) { }

        public ProgressOfUserImportIntoTenantQry(string tenant, string importId) : base(tenant)
        {
            ImportId = importId;
        }
    }

    public class StartImportOfUsersIntoTenantCmd : UserCmd<string>
    {
        public UserCreatedEvent[] Users { get; private set; }

        public StartImportOfUsersIntoTenantCmd() : base(null, null)
        {
        }

        public StartImportOfUsersIntoTenantCmd(UserCreatedEvent[] users)
            : this(null, null, users)
        {
        }

        public StartImportOfUsersIntoTenantCmd(string tenant, string userName, UserCreatedEvent[] users)
            : base(tenant, userName)
        {
            if (!users.AnyItems()) throw new DomainCommandException(this, "No users specified");

            Users = users;
        }
    }

    public class ImportOfUsersIntoTenantEvent : DomainEvent
    {
        public string ImportId { get; private set; }

        protected ImportOfUsersIntoTenantEvent() : base(null)
        {
        }

        protected ImportOfUsersIntoTenantEvent(string tenant, string importId)
            : base(tenant)
        {
            ImportId = importId;
        }
    }

    public class ImportOfUsersIntoTenantQueuedEvent : ImportOfUsersIntoTenantEvent
    {
        public ImportOfUsersIntoTenantQueuedEvent()
        {
        }

        public ImportOfUsersIntoTenantQueuedEvent(string tenant, string importId) : base(tenant, importId)
        {
        }
    }

    public class ImportOfUsersIntoTenantStartedEvent : ImportOfUsersIntoTenantEvent
    {
        public ImportOfUsersIntoTenantStartedEvent()
        {
        }

        public ImportOfUsersIntoTenantStartedEvent(string tenant, string importId)
            : base(tenant, importId)
        {
        }
    }

    /// <summary>
    /// The import batch has finished
    /// </summary>
    public class ImportOfUsersIntoTenantStagedEvent : ImportOfUsersIntoTenantEvent
    {
        public string FileNumber { get; private set; }
        public Exception Error { get; set; }
        public int ResultCount { get; set; }

        /// <summary>
        /// Unless u are a seriliser, dont use this!
        /// </summary>
        public ImportOfUsersIntoTenantStagedEvent()
        {
        }

        public ImportOfUsersIntoTenantStagedEvent(string tenant, string importId)
            : base(tenant, importId)
        {
        }
    }
}
