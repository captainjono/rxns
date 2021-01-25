using System;
using Rxns.CQRS;

namespace RxnsDemo.Micro.App.Cmds
{
    public class RecordAnswerForSurveyCmd : TenantCmd<Guid>
    {
        public string SurveyId { get; set; }
        public string UserId { get; set; }
        public string Answer { get; set; }
        public string QuestionId { get; set; }
    }
}
