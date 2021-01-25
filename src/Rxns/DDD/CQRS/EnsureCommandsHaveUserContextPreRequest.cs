using Rxns.DDD.Commanding;
using Rxns.CQRS;
using Rxns.DDD.BoundedContext;

namespace Rxns.DDD.CQRS
{
    public class EnsureCQRSHaveUserContextPreRequest<T> : IDomainCommandPreHandler<T> where T : class
    {
        public void Handle(T command)
        {
            //var context = new ThreadBasedUserContext(Thread.CurrentPrincipal);
            if (command is IRequireTenantContext)
            {
                var cmd = command as IRequireTenantContext;

                if (!cmd.HasTenantSpecified())
                    cmd.ForTenant("");//context.Tenant);
            }

            if (command is IRequireUserContext)
            {
                var cmd = command as IRequireUserContext;

                if (!cmd.HasUserSpecified())
                    cmd.ForUser(""); //context.UserName);
            }
        }
    }
}
