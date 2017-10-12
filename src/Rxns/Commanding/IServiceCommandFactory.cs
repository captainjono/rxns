using Rxns.Commanding;

namespace Rxns.Commanding
{
    public interface IServiceCommandFactory
    {
        IServiceCommand Get(string cmdName, params object[] constructorParams);
    }
}
