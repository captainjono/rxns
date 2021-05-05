using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using Rxns.DDD.Commanding;
using Rxns.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.DDD.CQRS
{

    public class LocallyExecutingCommandService : ICommandService, IRxnProcessor<IServiceCommand>, IRxnProcessor<IDomainQuery>, IRxnProcessor<IDomainCommand>
    {
        private readonly IDomainCommandMediator _cmdMediator;
        private readonly IDomainQueryMediator _qryMediator;
        private readonly IRxnManager<IRxn> _eventManager;
        private readonly MethodInfo _mediatorDef;
        private readonly Type _domainCmdType;
        private readonly MethodInfo _qryMediatorDef;
        private readonly ICommandFactory _cmdFactory;

        public LocallyExecutingCommandService(IDomainCommandMediator cmdMediator, IDomainQueryMediator qryMediator, IRxnManager<IRxn> eventManager, ICommandFactory cmdFactory)
        {
            _cmdMediator = cmdMediator;
            _qryMediator = qryMediator;
            _eventManager = eventManager;
            _cmdFactory = cmdFactory;

            _mediatorDef = _cmdMediator.GetType().GetRuntimeMethods().Skip(1).First();
            _domainCmdType = typeof(DomainCommandResult<>);
            _qryMediatorDef = _qryMediator.GetType().GetRuntimeMethods().Skip(1).First();
        }

        public IObservable<DomainCommandResult<T>> Run<T>(IDomainCommand<T> cmd)
        {
            return Rxn.Create<DomainCommandResult<T>>(o =>
            {
                try
                {
                    var cmdType = cmd.GetType().GetInterfaces().FirstOrDefault(w => w.FullName.Contains(typeof(IDomainCommand<>).FullName)).GenericTypeArguments[0];
                    var domainCmdType = _domainCmdType.MakeGenericType(cmdType);

                    var cmdAction = _mediatorDef //.GetMethod("SendAsync", new Type[] {})
                        .MakeGenericMethod(domainCmdType)
                        .Invoke(_cmdMediator, new object[] { cmd }) as IObservable<DomainCommandResult<T>>;

                    if (cmdAction == null) throw new DomainCommandException(cmd, "Something went wrong while trying to invoke the command: {0}".FormatWith(cmd.GetType()));

                    return cmdAction.Select(result =>
                    {
                        if (result == null) throw new DomainCommandException(cmd, "Something went wrong while invoking the command: {0}".FormatWith(cmd.GetType()));

                        return result;
                    })
                    .Subscribe(o);
                }
                catch (DomainCommandException)
                {
                    throw;
                }
                catch (TargetInvocationException e)
                {
                    throw e.GetBaseException();
                }
                catch (Exception e)
                {
                    throw new Exception("Fatal error while executing command '{0}': {1}".FormatWith(cmd.GetType(), e));
                }
            });
        }

        public IObservable<DomainQueryResult<T>> Run<T>(IDomainQuery<T> cmd)
        {
            return Rxn.Create<DomainQueryResult<T>>(o =>
            {
                try
                {
                    var qryType = cmd.GetType().GetInterfaces().FirstOrDefault(w => w.FullName.Contains(typeof(IDomainQuery<>).FullName)).GenericTypeArguments[0];
                    
                    var qryAction = _qryMediatorDef
                        .MakeGenericMethod(qryType)
                        .Invoke(_qryMediator, new object[] { cmd }) as IObservable<T>;

                    if (qryAction == null) throw new DomainQueryException(cmd, "Something went wrong while trying to invoke the query: {0}", cmd.GetType());

                    return qryAction
                            .Select(result =>
                            {
                                if (result == null) throw new DomainQueryException(cmd, "Something went wrong while invoking the query: {0}", cmd.GetType());

                                return new DomainQueryResult<T>(cmd.Id, result);
                            })
                            .Subscribe(o);
                }
                catch (DomainQueryException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new Exception("Fatal error while executing command '{0}': {1}".FormatWith(cmd.GetType(), e));
                }
            });
        }


        public IObservable<IRxn> Process(IServiceCommand @event)
        {
            return Run(@event).OfType<IRxn>();
        }


        public IObservable<object> Run(IServiceCommand cmd)
        {
            return Rxn.DfrCreate<object>(o =>
            {
                if (cmd is IDomainCommand)
                    return Process(cmd as IDomainCommand).Subscribe(o);

                if (cmd is IDomainQuery)
                    return Process(cmd as IDomainQuery).Subscribe(o);

                var cmdResultResource = _eventManager.CreateSubscription<ICommandResult>()
                                            .Where(c => c.InResponseTo == cmd.Id)
                                            .FirstOrDefaultAsync()
                                            
                                            .Subscribe(o);

                _eventManager.Publish(cmd.AsQuestion());

                return cmdResultResource;
            })
            .Finally(() => ReportStatus.Log.OnVerbose($"Finished command {cmd.Id}"))
            ;
        }

        public IObservable<ICommandResult> Run(string cmd)
        {
            return Run(_cmdFactory.FromString(cmd));
        }

        public IObservable<IRxn> Process(IDomainQuery @event)
        {
            //cant answer queries because they are not wrapped in a rxn. need a queryResult
             return Run(@event as IDomainQuery<dynamic>).Select(r => new DomainQueryResult<dynamic>(@event.Id, r));
        }

        public IObservable<IRxn> Process(IDomainCommand @event)
        {
            
            return Run(@event as TenantCmd<dynamic>);
        }
    }
}
