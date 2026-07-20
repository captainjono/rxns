using Rxns.DDD.Commanding;

namespace Rxns.DDD.Commanding
{
    public interface IServiceCommandFactory
    {
        IServiceCommand Get(string cmdName, params object[] constructorParams);
    }
}
