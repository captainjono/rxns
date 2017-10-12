using System;
using Rxns.Commanding;

namespace Rxns.Commanding
{
    public interface IServiceCommandHandler<in TCmd> where TCmd : IServiceCommand
    {
        IObservable<CommandResult> Handle(TCmd command);
    }
}
