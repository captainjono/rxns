﻿using System;
using Rxns.DDD.CQRS;

namespace RxnsDemo.Micro.App.Cmds
{
    public class BeginSurveyCmd : TenantCmd<Guid>
    {
        public string UserId { get; set; }
        public string AttemptId { get; set; }
    }
}
