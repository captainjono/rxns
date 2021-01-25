namespace Rxns.Microservices
{
    public interface IMicroServerRegistry
    {
    }

    public interface IMicroServerProxyRegistery : IMicroServerRegistry
    {
        void Register(string serivceTypeFullName);
    }

    public interface IMicroServerRegistryClient
    {
        bool Ping();
    }

   
}
