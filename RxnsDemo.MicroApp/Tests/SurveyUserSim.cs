using System;
using System.Reactive.Disposables;
using Rxns.Interfaces;
using RxnsDemo.Micro.App.Cmds;
using RxnsDemo.Micro.App.Qrys;

namespace RxnsDemo.Micro.Tests
{
    public class SurveyUserSim : IManageResources
    {
        IDisposable _resource = new CompositeDisposable();
        public enum Actions
        {
            Started,
            AnswersQuestion,
            ToiletBreak,
            ArrivesAtSurvey,
            SubmitSurvey,
            ThinkingOfAnswer,
            Distracted,
            Sneezes,
            LoosesTrainOfThought,
            GettingACoffee,
            WakesUp,
            LooksUpProgress,
            RunningForBus
        }

        public SurveyUser Id { get; }
        private readonly Action<IRxn> _onEvent;

        public SurveyUserSim(SurveyUser surveyUser, Action<IRxn> onEvent)
        {
            Id = surveyUser;
            _onEvent = onEvent;
        }
        public void Start()
        {
            _onEvent(new BeginSurveyCmd()
            {
                AttemptId = Id.AttemptId,
                UserId = Id.UserId
            });
        }

        public void Answer()
        {
            _onEvent(new RecordAnswerForSurveyCmd()
            {
                SurveyId = Id.AttemptId,
                Answer = GetRandomAnswer(),
                QuestionId = Guid.NewGuid().ToString(),
                UserId = Id.UserId
            });
        }

        public void End()
        {
            _onEvent(new FinishSurveyCmd()
            {
                SurveyId = Id.AttemptId,
                UserId = Id.UserId
            });
        }

        public void LookupScore()
        {
            _onEvent(new LookupProgressInSurveyQry()
            {
                AttemptId = Id.AttemptId,
                UserId = Id.UserId
            });
        }

        private string GetRandomAnswer()
        {
            return Guid.NewGuid().ToString();
        }

        public void Dispose()
        {
            _resource?.Dispose();
            _resource = null;
        }

        public void OnDispose(IDisposable obj)
        {
            _resource = obj;
        }
    }
}
