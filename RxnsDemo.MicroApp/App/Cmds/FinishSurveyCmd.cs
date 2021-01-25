using System;
using Rxns.CQRS;

namespace RxnsDemo.Micro.App.Cmds
{
    public class FinishSurveyCmd : TenantCmd<Guid>
    {
        public string UserId { get; set; }
        public string SurveyId { get; set; }
    }
}
