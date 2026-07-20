using Rxns.Logging;

namespace RxnsDemo.AzureB2C.RxnApps.Events
{
    public class ImportOfUserIntoTenantSuccessfulEvent : ImportOfUsersIntoTenantEvent
    {
        public string UserName { get; }
        public string Import { get; private set; }
        public string[] Log { get; private set; }

        public ImportOfUserIntoTenantSuccessfulEvent() { }
        public ImportOfUserIntoTenantSuccessfulEvent(string tenant, string importId, string userName, string[] log = null)
            : base(tenant, importId)
        {
            Log = log ?? new string[] { };
        }
    }

    public class ImportOfUserIntoTenantFailureEvent : ImportOfUsersIntoTenantEvent
    {
        public string UserName { get; }
        public string Error { get; set; }
        public string[] Log { get; private set; }

        public ImportOfUserIntoTenantFailureEvent() { }
        public ImportOfUserIntoTenantFailureEvent(string tenant, string importId, string userName, string error, string[] log = null)
            : base(tenant, importId)
        {
            UserName = userName;
            Error = error;
            Log = log ?? new string[] {};
        }
    }
}
