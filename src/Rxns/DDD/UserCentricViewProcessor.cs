using System;
using Rxns.DDD.Sql;
using Rxns.DDD.Tenant;
using Rxns.Interfaces.Reliability;

namespace Rxns.DDD
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
