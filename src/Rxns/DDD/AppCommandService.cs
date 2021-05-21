using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using Rxns.CQRS;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.DDD
{
    public interface IAppCommandService
    {
        /// <summary>
        /// This is a commanding interface for the microApp
        /// </summary>
        /// <param name="command"></param>
        /// <param name="route">the route to the remote app</param>
        IObservable<object> ExecuteCommand(string route, string command);
    }

    public class RxnManagerCommandService : ICommandService,  IAppCommandService//, IRxnProcessor<IDomainQuery>, IRxnProcessor<IDomainCommand>,
    {
        private readonly IRxnManager<IRxn> _eventmanager;
        private ICommandFactory _cmdFactory;
        private IServiceCommandFactory _srvCcmdFactory;

        public RxnManagerCommandService(IRxnManager<IRxn> eventmanager, ICommandFactory cmdFactory, IServiceCommandFactory svcCmdFactory)
        {
            _cmdFactory = cmdFactory;
            _eventmanager = eventmanager;
            _srvCcmdFactory = svcCmdFactory;
        }

        public IObservable<DomainQueryResult<T>> Run<T>(IDomainQuery<T> query)
        {
            return _eventmanager.Ask<DomainQueryResult<T>>(query);
        }

        public IObservable<DomainCommandResult<T>> Run<T>(IDomainCommand<T> cmd)
        {
            return _eventmanager.Ask<DomainCommandResult<T>>(cmd);
        }


        public IObservable<object> Run(IServiceCommand cmd)
        {
            return _eventmanager.Ask<IRxnResult>(cmd.AsQuestion());
        }

        public IObservable<ICommandResult> Run(string cmd)
        {
            return Rxn.DfrCreate<CommandResult>(() => Run(_cmdFactory.FromString(cmd)));
        }

        public IObservable<object> ExecuteCommand(string route, string command)
        {
            return Rxn.DfrCreate(() => ServiceCommand.Parse(command, _srvCcmdFactory)).SelectMany(c => c).SelectMany(c => Run(c));
        }
    }

    public class AppCommandService : ReportsStatus, IAppCommandService, IRxnPublisher<IRxn>, IRxnProcessor<CommandResult>
    {
        private readonly IResolveTypes _resolver;
        private readonly ICommandService _cmdService;
        private readonly IServiceCommandFactory _serviceCommands;
        private readonly IRxnManager<IRxn> _eventmanager;
        private readonly IAppStatusStore _appStatus;
        private Action<IRxn> _publish;
        private readonly IDictionary<string, string> _routes = new Dictionary<string, string>();

        public AppCommandService(IResolveTypes resolver, ICommandService cmdService, IServiceCommandFactory serviceCommands, IRxnManager<IRxn> eventmanager, IAppStatusStore appStatus)
        {
            _resolver = resolver;
            _cmdService = cmdService;
            _serviceCommands = serviceCommands;
            _eventmanager = eventmanager;
            _appStatus = appStatus;
        }

        public IObservable<object> ExecuteCommand(string route, string command)
        {
            //see what type of command we have?
            if (route.IsNullOrWhitespace() || !route.Contains("\\"))
                return ExecuteCommand(command); //a command to be executed by us
            else
                return SendClientCommand(route, command); //a command for a client

        }

        /// <summary>
        /// This method supports executing legacy commands as well as the newer
        /// type of serviceCommands
        /// </summary>
        /// <param name="command"></param>
        public IObservable<object> ExecuteCommand(string command)
        {
            try
            {
                return Rxn.DfrCreate(() => ServiceCommand.Parse(command, _serviceCommands)).SelectMany(c => c).SelectMany(c =>_cmdService.Run(c));
            }
            catch(ServiceCommandNotFound e)
            {
                var help = new StringBuilder();
                help.AppendLine("Path: {{tenant}}\\{{SystemName}} <-- Remote commands");
                help.AppendLine("      {{reporterName}}           <-- Local commands");
                help.AppendLine("      Empty                      <-- Local service commands");
                help.AppendLine();

                help.AppendLine("Registered service commands:");
                //var allServiceCommands = _resolver.ComponentRegistry.Registrations.Where(r => typeof(IServiceCommand).IsAssignableFrom(r.Activator.LimitType) && !r.Activator.LimitType.IsAbstract()).Select(r => r.Activator.LimitType).ToArray();
                //allServiceCommands.ForEach(c =>
                //{
                //    var cmdParams = c.GetProperties().Where(p => !p.IsDefined(typeof(IgnoreDataMemberAttribute), true)).ToArray();
                //    help.AppendLine("{0} {1}{2}".FormatWith(c.Name, cmdParams.Select(p => "<{0}".FormatWith(p.Name)).ToStringEach("> "), cmdParams.Any() ? ">" : ""));
                //});
                help.AppendLine();

                return CommandResult.Failure(help.ToString()).ToObservable();
            };
        }


        public IObservable<CommandResult> SendClientCommand(string route, string command, string from = null)
        {
            ServiceCommand.Parse(command, _serviceCommands).Select(c => c.AsQuestion(route)).ForEach(q => _appStatus.Add(q));

            return CommandResult.Success().ToObservable();
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }

        public IObservable<IRxn> Process(CommandResult @event)
        {
            OnInformation("[{1}]{0}", @event.Message, @event.Result == CmdResult.Success ? "SUCCESS" : "FAILURE");

            return Observable.Empty<IRxn>();
        }
    }
}
