using System;

namespace Rxns.CQRS
{
    public class UserCmd<T> : TenantCmd<T>, IRequireUserContext, IUserCmd
    {
        public UserCmd(string tenant, string userName) : base(tenant)
        {
            UserName = userName;
        }

        public string UserName { get; private set; }

        public bool HasUserSpecified()
        {
            return !UserName.IsNullOrWhitespace();
        }

        public void ForUser(string userName)
        {
            UserName = userName;
        }

    }

    public interface IUserCmd
    {
        string UserName { get; }
        string Tenant { get; }
    }
}
