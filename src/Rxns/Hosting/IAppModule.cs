using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IAppModule : IModule
    {
        IRxnLifecycle Load(IRxnLifecycle lifecycle);
    }

    public interface IRxnLifecycleFactory
    {
        IRxnLifecycle Create();
    }
}
