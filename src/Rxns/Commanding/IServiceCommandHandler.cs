using System;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.Commanding
{
    public interface IServiceCommandHandler<in TCmd> where TCmd : IServiceCommand 
    {
        IObservable<CommandResult> Handle(TCmd command);
    }
}
