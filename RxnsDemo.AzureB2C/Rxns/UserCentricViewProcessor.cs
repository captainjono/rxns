using System;
using Rxns.Interfaces.Reliability;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class UserCentricViewProcessor : BatchingViewProcessor
    {
        public UserCentricViewProcessor(ITenantDatabaseFactory contextFactory, IReliabilityManager reliably)
            : base(contextFactory, reliably)
        {
        }

        public Guid GetUserId(string userName, IOrmContext batch)
        {
            var userContext = new SqlMembershipBasedUserContext(batch);
            var userId = userContext.GetUserId(userName);

            return userId;
        }

        public Guid GetRoleId(string roleName, IOrmContext batch)
        {
            var userContext = new SqlMembershipBasedUserContext(batch);
            var userId = userContext.GetRole(roleName);

            return userId;
        }
    }
}
