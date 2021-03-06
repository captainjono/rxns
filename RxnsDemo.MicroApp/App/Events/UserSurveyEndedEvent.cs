﻿using System;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.Micro.App.Events
{
    public class UserSurveyEndedEvent : DomainEvent
    {
        public string UserId { get; set; }
        public string SurveyId { get; set; }
        public DateTime At { get; set; }
    }
}
