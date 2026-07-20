namespace Rxns.DDD.CQRS
{
    public interface IRequireUserContext : IRequireTenantContext
    {
        string UserName { get; }
        bool HasUserSpecified();
        void ForUser(string userName);
    }
}
