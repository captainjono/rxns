using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns;
using Rxns.Interfaces;
using Rxns.Logging;
using Stateless;

namespace RxnsDemo.Micro.Tests
{
    public enum SurveyState
    {
        StartingSoon,
        Started,
        PausedSurvey,
        Finished,
    }

    public class UsersUsingTheSurveySimulator : ReportsStatus
    {
        private readonly TimeSpan _SurveyLength;
        private readonly int _maxusers;
        private readonly TimeSpan _userSpeed;
        private readonly IScheduler _forWorkflowGenerator;
        private readonly Random _random;

        public UsersUsingTheSurveySimulator(TimeSpan SurveyLength, int maxusers, int workflowSeed, TimeSpan userSpeed, IScheduler forWorkflowGenerator = null)
        {
            _SurveyLength = SurveyLength;
            _maxusers = maxusers;
            _userSpeed = userSpeed;
            _forWorkflowGenerator = forWorkflowGenerator ?? Scheduler.Default;
            _random = new Random(workflowSeed);
        }

        public IObservable<IRxn> Start()
        {
            return Rxn.DfrCreate<IRxn>(o =>
            {
                var resources = new CompositeDisposable();

                Func<TimeSpan> randomDelay = () => TimeSpan.FromSeconds(_random.Next(5, 10));

                DoSurveyWorkflow(_maxusers, randomDelay)
                    .Subscribe(o.OnNext, o.OnError)
                    .DisposedBy(resources);

                new DisposableAction(o.OnCompleted).DisposedBy(resources);

                return resources;
            });
        }

        private IObservable<IRxn> DoSurveyWorkflow(int userCount, Func<TimeSpan> giveuserPersonality)
        {
            return Rxn.Create<IRxn>(o =>
            {
                var endTest = new CompositeDisposable();
                OnInformation($"Building virtual Survey of '{userCount}' users");

                //var shared = new SameNumberForFalseCompares(() => DateTime.Now.Ticks);

                var usersEnrolled = 0;
                foreach (var user in Generateusers(userCount, Guid.NewGuid().ToString(), o.OnNext))
                {
                    var whatHappens = SimulateuserOnSurveyDay(user, _SurveyLength);

                    TakeSurvey(user, whatHappens, giveuserPersonality());

                    if (++usersEnrolled % 10 == 0) //dont spam info
                        OnVerbose($"Enrolled {usersEnrolled} so far");
                }

                return endTest; //dont want to limit Survey lengths by collecting lists of users -> mem consumption!
            });
        }

        /// <summary>
        /// Dispose the surveyUser to end the Survey
        /// </summary>
        /// <param name="surveyUser"></param>
        /// <param name="SurveyWf"></param>
        /// <param name="userDelay"></param>
        private void TakeSurvey(SurveyUserSim surveyUser, StateMachine<SurveyState, SurveyUserSim.Actions> SurveyWf, TimeSpan userDelay)
        {
            OnInformation($"[{surveyUser.Id.AttemptId}] 'Waking {surveyUser.Id.UserName} up for Survey");

            Observable.Timer(userDelay, _userSpeed, _forWorkflowGenerator).Subscribe(this, _ =>
            {
                try
                {
                    var nextSteps = SurveyWf.PermittedTriggers.ToArray();

                    if (nextSteps.Length < 1) return;

                    var nextStep = nextSteps[_random.Next(0, nextSteps.Length)];
                    OnVerbose($"[{surveyUser.Id.AttemptId}][{surveyUser.Id.UserName}] is about to {nextStep}");

                    SurveyWf.Fire(nextStep);
                }
                catch (Exception e)
                {
                    OnError(e, "Failed to move to next state. A race condition exists at the end of the test causing this");
                }
            }).DisposedBy(surveyUser);
        }

        private StateMachine<SurveyState, SurveyUserSim.Actions> SimulateuserOnSurveyDay(SurveyUserSim surveyUser, TimeSpan SurveyLength)
        {
            var userLife = new StateMachine<SurveyState, SurveyUserSim.Actions>(SurveyState.StartingSoon);
            var startedAt = DateTime.Now;

            //model different aspects in different functions to allow reuse?
            //userLife.Configure(SurveyWf.EnrollsInCourse)

            //pre Survey workflow
            userLife.Configure(SurveyState.StartingSoon)
                //.PermitReentry(SurveyUserSim.Actions.WakesUp)
                //.PermitReentry(SurveyUserSim.Actions.GettingACoffee)
                //.PermitReentry(SurveyUserSim.Actions.RunningForBus)
                .Permit(SurveyUserSim.Actions.ArrivesAtSurvey, SurveyState.Started)
                ;

            //how the surveyUser behaves during the Survey
            userLife.Configure(SurveyState.Started)
                .PermitReentry(SurveyUserSim.Actions.AnswersQuestion)
                //   .PermitReentry(SurveyUserSim.Actions.ThinkingOfAnswer)
                //   .PermitReentryIf(SurveyUserSim.Actions.Distracted, () => !PossiblyFinished(SurveyLength, startedAt))
                //    .PermitReentry(SurveyUserSim.Actions.Sneezes)
                .PermitReentry(SurveyUserSim.Actions.LooksUpProgress)
                .OnEntryFrom(SurveyUserSim.Actions.AnswersQuestion, surveyUser.Answer)
                .OnEntryFrom(SurveyUserSim.Actions.LooksUpProgress, surveyUser.LookupScore)
                .OnEntryFrom(SurveyUserSim.Actions.ArrivesAtSurvey, surveyUser.Start)
                //  .PermitIf(SurveyUserSim.Actions.ToiletBreak, SurveyState.PausedSurvey, () => !PossiblyFinished(SurveyLength, startedAt)) //need to weight this less then the others
                .PermitIf(SurveyUserSim.Actions.SubmitSurvey, SurveyState.Finished, () => PossiblyFinished(SurveyLength, startedAt))
                ;

            //how the surveyUser behaves during the pause
            userLife.Configure(SurveyState.PausedSurvey)
                .SubstateOf(SurveyState.Started)
                .PermitReentry(SurveyUserSim.Actions.ToiletBreak)
                .Permit(SurveyUserSim.Actions.Started, SurveyState.Started)
                ;

            //what happens after the Survey
            userLife.Configure(SurveyState.Finished)
                .SubstateOf(SurveyState.Started)
                .OnEntryFrom(SurveyUserSim.Actions.SubmitSurvey, () =>
                {
                    surveyUser.End();
                    surveyUser.Dispose();

                    OnInformation($"[{surveyUser.Id.UserName}][{surveyUser.Id.AttemptId}] submitted Survey");
                });

            return userLife;
        }

        private bool PossiblyFinished(TimeSpan SurveyLength, DateTime startedAt)
        {
            return (DateTime.Now - startedAt) > SurveyLength;
        }

        private IEnumerable<SurveyUserSim> Generateusers(int userCount, string attemptId, Action<IRxn> onEvent)
        {
            return Enumerable.Range(0, userCount)
                .Select(_ => new SurveyUserSim(new SurveyUser()
                {
                    AttemptId = attemptId,
                    UserId = GetRandonString("UserId"),
                    UserName = GetRandonString("User")
                }, onEvent));
        }
        private string GetRandonString(string seed)
        {
            return "{0}_{1}".FormatWith(seed, Guid.NewGuid().ToStringMax(5));
        }
    }
}
