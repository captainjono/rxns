using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
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

        /// <summary>
        /// Same as <see cref="ExecuteCommand(string, string)"/> but threads the
        /// originator's route through to <c>SendClientCommand</c> so the
        /// resulting CommandResult can be routed back via
        /// <see cref="IAppCmdManager.ResolveAndForgetOriginator"/>. EventsHub
        /// uses this when a SignalR client invokes the SendCommand Hub method
        /// — phase 7n4 fix for the UI "NO ROUTE/SOURCE" hang.
        /// </summary>
        IObservable<object> ExecuteCommand(string route, string command, string from);
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

        // RxnManagerCommandService is the legacy in-process path; it has no
        // multi-channel routing concept so the originator just gets ignored.
        public IObservable<object> ExecuteCommand(string route, string command, string from) =>
            ExecuteCommand(route, command);
    }

    public class AppCommandService : ReportsStatus, IAppCommandService,  IRxnProcessor<CommandResult>
    {
        private readonly ICommandService _cmdService;
        private readonly IServiceCommandFactory _serviceCommands;
        private readonly IAppCmdManager _appStatus;

        public AppCommandService(ICommandService cmdService, IServiceCommandFactory serviceCommands, IAppCmdManager appStatus)
        {
            _cmdService = cmdService;
            _serviceCommands = serviceCommands;
            _appStatus = appStatus;
        }

        public IObservable<object> ExecuteCommand(string route, string command)
        {
            return ExecuteCommand(route, command, from: null);
        }

        // Overload that lets callers pass the originator's registered route
        // through to SendClientCommand so the cmd manager can track who
        // issued the cmd. EventsHub.SendCommand resolves Context.ConnectionId
        // → route and calls this overload. Phase 7n4 fix for the UI
        // sendCommand "NO ROUTE/SOURCE" path.
        public IObservable<object> ExecuteCommand(string route, string command, string from)
        {
            if (route.IsNullOrWhitespace() || !route.Contains("\\"))
            {
                // Local cmd path (Reload, Export, etc. that the arena
                // executes itself). We still need to track the originator
                // so the CommandResult gets routed back to the UI client
                // that issued the cmd. Parse → track each command's Id →
                // run. Phase 7n4 fix: previously this branch dropped
                // `from`, so UI Reload showed "NO ROUTE" in the result-back
                // diagnostic.
                return Rxn.DfrCreate(() => ServiceCommand.Parse(command, _serviceCommands))
                    .SelectMany(c => c)
                    .Do(c =>
                    {
                        if (!string.IsNullOrEmpty(from) && c is IUniqueRxn u)
                            _appStatus.TrackOriginator(u.Id, from);
                    })
                    .SelectMany(c => _cmdService.Run(c));
            }
            return SendClientCommand(route, command, from);
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
            // Track the originator BEFORE Add — Add iterates channels and may
            // dispatch synchronously in-process; the worker's result could
            // race back here before tracking is in place. Phase 7n4 fix: UI
            // sendCommand path used to bypass tracking entirely; now the
            // calling Hub passes its registered route via `from` and we
            // record the cmd→originator mapping on IAppCmdManager so the
            // result can be routed back regardless of which channel it
            // arrives on. `from` may be null for callers that don't have a
            // route (legacy in-process); in that case the result is
            // delivered locally via _rxnManager.Publish only.
            foreach (var q in ServiceCommand.Parse(command, _serviceCommands).Select(c => c.AsQuestion(route)))
            {
                if (!string.IsNullOrEmpty(from))
                    _appStatus.TrackOriginator(q.Id, from);
                _appStatus.Add(q);
            }

            return CommandResult.Success().ToObservable();
        }

        public IObservable<IRxn> Process(CommandResult @event)
        {
            OnInformation("[{1}]{0}", @event.Message, @event.Result == CmdResult.Success ? "SUCCESS" : "FAILURE");

            return Observable.Empty<IRxn>();
        }
    }
}
