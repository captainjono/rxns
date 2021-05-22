namespace Rxns.DDD.BoundedContext
{
    public interface ITenantContextFactory
    {
        /// <summary>
        /// falls back to the implict context of the THreads IPrinciple if username not specified
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        //IRxnUserContext GetUserContext(string tenant, string userName = null);
       // ITenantContext GetContext(string tenant);
    }
}
