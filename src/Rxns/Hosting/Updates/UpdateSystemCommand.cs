using System;
using Rxns.DDD.Commanding;

namespace Rxns.Commanding
{
    public class UpdateSystemCommand : RxnQuestion, IServiceCommand
    {
        public string Id { get; set; }
        public string SystemName { get; set; }
        public string Version { get; set; }
        public string Reactor { get; set; }

        public UpdateSystemCommand()
        {

        }

        public UpdateSystemCommand(string systemName, string version, string destinationRoute)
        {
            Destination = destinationRoute;
            SystemName = systemName;
            Version = version;
            Id = Guid.NewGuid().ToString();
            Options = $"{nameof(UpdateSystemCommand)} {systemName} {version} {destinationRoute}";
        }

        public UpdateSystemCommand(string systemName, string reactor, string version, string destinationRoute) : this(systemName, version, destinationRoute)
        {
            Reactor = reactor;
            Options = $"{nameof(UpdateSystemCommand)} {systemName} {reactor} {version} {destinationRoute}";
        }
    }
}
