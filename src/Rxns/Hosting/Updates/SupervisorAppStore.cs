using System;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;

namespace Rxns.Hosting.Updates
{
    public class SupervisorAppUpdateProvider : IStoreAppUpdates
    {
        private readonly ICommandService _cmdService;

        public SupervisorAppUpdateProvider(ICommandService cmdService)
        {
            _cmdService = cmdService;
        }

        public IObservable<string> Run(GetAppDirectoryForAppUpdate command)
        {
            return _cmdService.Run(command).OfType<CommandResult>().Select(r => r.Message);
        }

        public IObservable<string> Run(PrepareForAppUpdate command)
        {
            return _cmdService.Run(command).OfType<CommandResult>().Select(r => r.Message);
        }

        public IObservable<string> Run(MigrateAppToVersion command)
        {
            return _cmdService.Run(command).OfType<CommandResult>().Select(r => r.Message);
        }
    }
}
