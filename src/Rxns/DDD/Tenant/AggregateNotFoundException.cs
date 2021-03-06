﻿using System;

namespace Rxns.DDD.Tenant
{
    public class AggregateNotFoundException : Exception
    {
        public string AggregateId { get; private set; }

        public AggregateNotFoundException(string aggregateId, string message, Exception inner) : base(message, inner)
        {
            AggregateId = aggregateId;
        }

        public AggregateNotFoundException(string aggregateId, string message) : base(message)
        {
            AggregateId = aggregateId;
        }

        public AggregateNotFoundException(string message) : base(message)
        {
        }
    }
}
