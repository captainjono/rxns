using Rxns.DDD.BoundedContext;
using RxnsDemo.Micro.App.Models;

namespace RxnsDemo.Micro.App.Events
{
    public class UserAnsweredQuestionEvent : DomainEvent
    {
        public string UserId { get; set; }
        public string SurveyId { get; set; }
        public AnswerModel Answer { get; set; }
    }
}
