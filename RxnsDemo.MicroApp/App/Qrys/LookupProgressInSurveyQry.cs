using Rxns.DDD.CQRS;
using RxnsDemo.Micro.App.Models;

namespace RxnsDemo.Micro.App.Qrys
{
    public class LookupProgressInSurveyQry : TenantQry<SurveyProgressModel>
    {
        public string UserId { get; set; }
        public string AttemptId { get; set; }
    }
}
