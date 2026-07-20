using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace RxnsDemo.Micro.Tests
{
    //use an inFirectoryBased(repo) that allows visualation of the tape files with templated anuglar selectors so u can review tapes and then load them into plots also
    //use that repo, replacing the sql repo
    //create an auto-tuner for services which measure the httptimeout periods and display them on the metrics report. ms:fraction of time reached so far = 100%
    public class Basic30UserSurveySimulation : ReportStatusService, IRxnCfg
    {
        private readonly ICommandService _cmds;
        private IDisposable _endSurvey;

        public string Reactor => "SimulatedSurvey";


        public Basic30UserSurveySimulation(ICommandService cmds)
        {
            _cmds = cmds;
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.Create(() =>
            {
                _endSurvey = Rxn.Create(TimeSpan.FromSeconds(20), () => Start()).Subscribe();

                return CommandResult.Success("Survey will start begin in 20 seconds");
            });
        }

        public override IObservable<CommandResult> Stop(string @from = null)
        {
            return Rxn.Create(() => 
            {
                _endSurvey?.Dispose();

                return CommandResult.Success();
            });
        }

        public IDisposable Start()
        {
            var surveyLoop = new EventLoopScheduler();
            var surveySim = new UsersUsingTheSurveySimulator(TimeSpan.FromMinutes(60), 3, 2233, TimeSpan.FromSeconds(3), surveyLoop);
            var testSessionLogStream = this.ReportsOn(surveySim);

            return surveySim.Start()
                .Do(cmd =>
                {
                   IObservable<object> userPerfomingAnAction = _cmds.Run((dynamic) cmd);
                   userPerfomingAnAction.Subscribe(surveySim, result => { /*finished! */ });
                })
                .Catch<IRxn, Exception>(e =>
                {
                    surveySim.OnError(e);
                    return Rxn.Empty<IRxn>();
                })
                .FinallyR(() => testSessionLogStream.Dispose())
                .Subscribe();
        }


        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; }
        public RxnMode Mode { get; }
    }

    public class SurveyUser
    {
        public string UserName { get; set; }
        public string UserId { get; set; }
        public string AttemptId { get; set; }
    }

   
}
