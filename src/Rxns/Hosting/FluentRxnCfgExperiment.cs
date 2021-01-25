using System;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    class FluentRxnCfgExperiment
    {
        public static object TestServicesBuildFluently()
        {
            return new RxnAppBuilder()
                .ThatUsesCQRS()
                .WithCommands()
                .HandledBy<DomainCommandMediator>();
            ////  .Supporting(RxnCfg.Cmd<StudentAnswersDomainService, RecordAnswerForAttemptCmd, Guid>())
            //  //.WithPreRequestHandlers(new RequestTimer())
            //  //.WithPostRequesthandlers(new ResponseCache())
            //  .WithQuerys()
            //  .HandledBy<DomainQueryMediator>()
            //  .Supporting(RxnCfg.Qry<StudentAnswersDomainService, LookupStudentProgressInTest, StudentProgressModel>())
            //  //.WithPreRequestHandlers(new RequestLogger())
            //  //.WithPostRequesthandlers(new ResponseCache())
            //  .And()
            //  .ThatUsesEventSourcing()
            //  .SupportingEventsOfType<IRxn>()
            //  .TransportedBy<LocalBackingChannel<IRxn>>()
            //  //.TransportedBy<AzureQueueBackingChannel<IRxn>>()  
            //  //.TransportedBy<RabbitMQBackingChannel<IRxn>>()  
            //  .And()
            //  .WithScheduler()
            //  .ThatMonitorsFileForTasks("tasks.json")
            //  .ThatRunsTheseTasks()
            //  //   .ThatRunsTheseTasks(RxnCfg.Task<SqlBackupTask>())
            //  // .And();
            //  //   .WithMetricsReportingTo(IReportingService)//
            //  .WithReactors()
            //  .ForFeature("AssessmentEvents")
            //  .WithProcessors( /* ES'd with signature IObservable<IRxn> Process(IRxn event) */)
            //  .And()
            //  .And();
        }
    }
    public class RxnAppBuilder
    {

        public RxnAppBuilder ForApp(IMicroApp app)
        {
            App = app;

            return this;
        }

        public RxnCQRSBuilder ThatUsesCQRS()
        {
            return CQRS = new RxnCQRSBuilder(this);
        }

        public RxnEsBuilder ThatUsesEventSourcing()
        {
            return ES = new RxnEsBuilder(this);
        }

        public RxnAppBuilder ThatResolvesTypesWith(IResolveTypes container)
        {
            Container = container;
            return this;
        }


        public IMicroApp App { get; private set; }
        public IResolveTypes Container { get; private set; }
        public RxnCQRSBuilder CQRS { get; private set; }
        public RxnEsBuilder ES { get; private set; }
        public Type Setup { get; set; }
        public Type[] SupportedEvents => ES != null ? ES.SupportedEvents : new Type[] { };

        public RxnAppBuilder InstalledBy<T>() where T : IAppSetup
        {
            Setup = typeof(T);

            return this;
        }

        public RxnSchedulerBuilder WithScheduler()
        {
            return new RxnSchedulerBuilder(this);
        }

        public RxnReactorBuilder WithReactors()
        {
            return new RxnReactorBuilder(this);
        }

        public RxnAppBuilder WithMetricsReportingTo(object reportingService)
        {
            return this;
        }
    }

    public class RxnReactorBuilder
    {
        private RxnAppBuilder _rxnAppBuilder;

        public RxnReactorBuilder(RxnAppBuilder rxnAppBuilder)
        {
            _rxnAppBuilder = rxnAppBuilder;
        }

        public RxnReactorServicesBuilder ForFeature(string name)
        {
            return new RxnReactorServicesBuilder(this);
        }

        public RxnAppBuilder And()
        {
            return _rxnAppBuilder;
        }
    }

    public class RxnReactorServicesBuilder
    {
        private RxnReactorBuilder _rxnReactorBuilder;

        public RxnReactorServicesBuilder(RxnReactorBuilder rxnReactorBuilder)
        {
            _rxnReactorBuilder = rxnReactorBuilder;
        }

        public RxnReactorServicesBuilder WithProcessors(params IRxnProcessor<dynamic>[] processors)
        {
            return this;
        }

        public RxnReactorServicesBuilder WithPulsars(params IRxnProcessor<dynamic>[] processors)
        {
            return this;
        }

        public RxnReactorBuilder And()
        {
            return _rxnReactorBuilder;
        }
    }

    public class RxnSchedulerBuilder
    {
        private readonly RxnAppBuilder _builder;

        public RxnSchedulerBuilder(RxnAppBuilder builder)
        {
            _builder = builder;
        }

        public RxnSchedulerBuilder ThatMonitorsFileForTasks(string filenameJson)
        {
            return this;
        }

        public RxnAppBuilder ThatRunsTheseTasks(params object[] tasks)
        {
            return _builder;
        }
    }

    public class RxnEsBuilder
    {
        private readonly RxnAppBuilder _builder;
        public Type[] SupportedEvents;
        public Type BackingChannel;

        public RxnEsBuilder(RxnAppBuilder builder)
        {
            _builder = builder;
        }

        public RxnEsBuilder SupportingEventsOfType<T>()
        {
            SupportedEvents = new[] { typeof(T) };
            return this;
        }

        public RxnEsBuilder TransportedBy<T>()
        {
            BackingChannel = typeof(T);
            return this;
        }

        public RxnAppBuilder And()
        {
            return _builder;
        }
    }

    public class RxnCQRSBuilder
    {
        private readonly RxnAppBuilder _builder;

        public RxnCQRSBuilder(RxnAppBuilder builder)
        {
            _builder = builder;
        }

        public RxnMediatorBuilder<IDomainCommandMediator> WithCommands()
        {
            return new RxnMediatorBuilder<IDomainCommandMediator>(this);
        }

        public RxnMediatorBuilder<IDomainQueryMediator> WithQuerys()
        {
            return new RxnMediatorBuilder<IDomainQueryMediator>(this);
        }

        public RxnAppBuilder And()
        {
            return _builder;
        }
    }

    public class RxnMediatorBuilder<T> where T : IMediator
    {
        private readonly RxnCQRSBuilder _builder;
        public object[] _cmds;
        public T _mediator;
        private Type _mediatort;

        public RxnMediatorBuilder(RxnCQRSBuilder builder)
        {
            _builder = builder;
        }

        public RxnMediatorBuilder<T> HandledBy(T mediator)
        {
            _mediator = mediator;
            return this;
        }

        public RxnMediatorBuilder<T> HandledBy<TT>() where TT : T
        {
            _mediatort = typeof(TT);
            return this;
        }

        public RxnCQRSBuilder Supporting(params CQRSRequest[] cmds)
        {
            //_cmds = cmds;
            return _builder;
        }

        public RxnCQRSBuilder And()
        {
            return _builder;
        }
    }
}
