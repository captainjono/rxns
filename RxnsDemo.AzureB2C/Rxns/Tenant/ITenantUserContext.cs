using System;
using Rxns.DDD.BoundedContext;
using Rxns.Interfaces;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class RvRole
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; }
        public bool IsExternal { get; set; }
    }

    public class UserCreatedEvent : UserDomainEvent
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
    }

    public interface ITenantUserContext
    {
        string[] GetUsers();
        Guid GetUserId(string userName);
        bool TryGetUserId(string username, out Guid userId);
        string[] GetRoles(Guid userId);
        RxnsRole[] GetRoles();
        string GetName(Guid userName);
        string GetEmail(Guid userName);
        Guid RegisterUser(UserCreatedEvent userName);
    }
}
