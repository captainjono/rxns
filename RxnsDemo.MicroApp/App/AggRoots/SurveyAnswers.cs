using System;
using System.Collections.Generic;
using Rxns.DDD.BoundedContext;
using RxnsDemo.Micro.App.Events;
using RxnsDemo.Micro.App.Models;

namespace RxnsDemo.Micro.App.AggRoots
{
    public interface ISurveyAnswer : IDomainEvent
    {
        string userId { get; }
        string AttemptId { get; }
    }

    public class SurveyAnswers : AggRoot
    {
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }

        public string SurveyId { get; set; }
        public string AttemptId { get; set; }

        public IList<AnswerModel> Answers { get; set; }

        public SurveyAnswers()
        {
            Answers = new List<AnswerModel>();
        }

        public SurveyAnswers(string surveyId) : base()
        {
            SurveyId = surveyId;
        }

        public void Start(string userId, string attemptId, DateTime? time = null)
        {
            SurveyId = userId;
            AttemptId = attemptId;
            StartedAt = time ?? DateTime.Now;

            LogChange(new UserSurveyStartedEvent()
            {
                SurveyId = AttemptId,
                UserId = SurveyId,
                At = EndedAt
            });
        }

        public void Record(AnswerModel answer)
        {
            Answers.Add(answer);

            LogChange(new UserAnsweredQuestionEvent()
            {
                SurveyId = AttemptId,
                UserId = SurveyId,
                Answer = answer
            });
        }

        public void End(DateTime? time = null)
        {
            EndedAt = time ?? DateTime.Now;

            LogChange(new UserSurveyEndedEvent()
            {
                SurveyId = AttemptId,
                UserId = SurveyId,
                At = EndedAt
            });
        }

        public void ApplyChange(UserAnsweredQuestionEvent @event)
        {
            Record(@event.Answer);
        }

        public void ApplyChange(UserSurveyStartedEvent @event)
        {
            Start(@event.UserId, @event.SurveyId, @event.At);
        }

        public void ApplyChange(UserSurveyEndedEvent @event)
        {
            End(@event.At);
        }
    }
}
