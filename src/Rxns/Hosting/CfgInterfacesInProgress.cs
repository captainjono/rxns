using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns
{
    public static class RxnCfg
    {
        public static CQRSRequest Cmd<T, TP, TR>() where T : IDomainCommandHandler<TP, TR> where TP : IDomainCommand<TR>
        {
            return new CQRSRequest { Handler = typeof(T), Request = typeof(TR) };
        }

        public static CQRSRequest Qry<T, TP, TR>() where T : IDomainQueryHandler<TP, TR> where TP : IDomainQuery<TR>
        {
            return new CQRSRequest { Handler = typeof(T), Request = typeof(TR) };
        }
    }

    public class CQRSRequest
    {
        public Type Handler { get; set; }

        public Type Request { get; set; }
    }
}
