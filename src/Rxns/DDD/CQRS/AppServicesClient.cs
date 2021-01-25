using Rxns.Hosting;

namespace Rxns.DDD.CQRS
{
    public abstract class AppServicesClient : AuthenticatedServiceClient
    {
        protected AppServicesClient(CommandServiceClientCfg configuration, IHttpConnection connection)
            : base(connection)
        {
            if (configuration != null) SetConfiguration(configuration);
        }

        protected abstract void SetConfiguration(CommandServiceClientCfg cfg);
    }
}
