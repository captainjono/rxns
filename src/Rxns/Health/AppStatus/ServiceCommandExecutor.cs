using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health.AppStatus
{
    public class ServiceCommandExecutor : ReportsStatus, IRxnProcessor<IRxnQuestion>, IRxnProcessor<IServiceCommand> //this should probably be deprecated and replace by a IServiceCommandHandler
    {
        private readonly IServiceCommandFactory _cmdFactory;
        private readonly IResolveTypes _resolver;
        private readonly ICommandService _cmds;
        private readonly IRouteProvider _local;
        private readonly IAppStatusStore _statusStore;
        private Type[] _respondsToRoute;

        public ServiceCommandExecutor(IServiceCommandFactory cmdFactory, IResolveTypes resolver, ICommandService cmds, IRxnAppCfg cfg, IRouteProvider local, IAppStatusStore statusStore = null)
        {
            _cmdFactory = cmdFactory;
            _resolver = resolver;
            _cmds = cmds;
            _local = local;
            _statusStore = statusStore;


            if(cfg.Args.AnyItems())
                _respondsToRoute = RxnCreator.DiscoverRoutes(cfg.Args.Last().ToLower(), resolver);
            //else
                //_respondsToRoute = new Type[] { typeof(IRxn) };
        }

        private IObservable<dynamic> RunIt(dynamic cmd)
        {
            return (IObservable<dynamic>)_cmds.Run(cmd);
        }

        public IObservable<IRxn> Process(IServiceCommand @event)
        {
            return Run(@event);
        }

        /// <summary>
        /// this is a pretty crap implementation because of the fact that every class needs to resolve the same command
        /// and possibly fail it. realitistically it should resolve the command somewhere else then feed broadcast a service
        /// command event which then gets picked up by the handlers? At a small scale, it doesnt matter.. its can just be improved
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public IObservable<IRxn> Process(IRxnQuestion command)
        {
            OnVerbose("Saw: {0}", command.Options);
            if (!command.Destination.IsNullOrWhitespace() && command.Destination != _local.GetLocalBaseRoute())
            {
                if (_statusStore == null) return null;

                OnWarning($"Queued command to {command.Destination} because its not local {_local.GetLocalBaseRoute()}");
                _statusStore.Add(command);
                return null;
            };

            var cmdToRun = ServiceCommand.Parse(command.Options, _cmdFactory);
            cmdToRun.Id = command.Id;//use original msg id

            return Run(cmdToRun);
        }

        private IObservable<IRxn> Run(IServiceCommand cmdToRun)
        {
            if (cmdToRun is IDomainQuery || cmdToRun is IDomainCommand)
            {
                if (!_respondsToRoute.Any(r => r == cmdToRun.GetType()))
                    return null;

                OnInformation("Asking: {0}", cmdToRun.GetType().Name);

                return RunIt(cmdToRun).Select(r => new DomainQueryResult<dynamic>(cmdToRun.Id, r.Result));
            }

            OnInformation("Running: {0}", cmdToRun.GetType().Name);

            return Rxn.Create<IRxn>(o =>
            {

                return Rxn.Create(() =>
                {

                    try
                    {

                        var cmdInterface = typeof(IServiceCommandHandler<>).MakeGenericType(cmdToRun.GetType()).MakeArrayType();
                        var handlers = _resolver.Resolve(cmdInterface) as IEnumerable<object>;

                        foreach (var handler in handlers)
                        {
                            var handleMethod = handler.InvokeReliably("Handle", cmdToRun) as IObservable<CommandResult>;
                            return handleMethod.Select(result =>
                            {
                                if (result.Result == CmdResult.Failure)
                                    OnWarning("Command '{0}' ran with '{1}' because '{2}", cmdToRun.GetType().Name, result.Result, result.Message);
                                else
                                    OnVerbose("Command '{0}' ran with '{1}'", cmdToRun.GetType().Name, result.Result);

                                return result;

                            })
                            .Catch<CommandResult, Exception>(e => CommandResult.Failure(e.Message).ToObservable());
                        }

                        if (!handlers.AnyItems())
                        {
                            OnWarning("No handler found for {0}", cmdToRun.GetType().Name);
                        }
                    }
                    catch (ServiceCommandNotFound e)
                    {
                        CommandResult.Failure("No handlers were found for the command".FormatWith(cmdToRun == null ? "unknown" : cmdToRun.GetType().Name, e.Message)).ToObservable();
                    }
                    catch (Exception e)
                    {
                        CommandResult.Failure("Command '{0}' threw an error '{1}'".FormatWith(cmdToRun == null ? "unknown" : cmdToRun.GetType().Name, e.Message)).ToObservable();
                    }

                    return null;
                }).Subscribe(o);
            });
        }

    }
}
