using System;

namespace Rxns.DDD.CQRS
{
    public class UserQry<T> : TenantQry<T>, IRequireUserContext
    {
        public string UserName { get; private set; }

        public UserQry(string tenant, string userName) : base(tenant)
        {
            UserName = userName;
        }
        
        public bool HasUserSpecified()
        {
            return !String.IsNullOrWhiteSpace(UserName);
        }

        public void ForUser(string userName)
        {
            UserName = userName;
        }
    }
}
