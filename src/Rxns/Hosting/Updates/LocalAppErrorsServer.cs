using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using Ionic.Zip;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public class LocalAppStatusServer : IAppStatusServiceClient
    {
        private readonly IAppErrorManager _errorMgr;
        private readonly IAppStatusManager _appStatusMgr;

        public LocalAppStatusServer(IAppErrorManager errorMgr, IAppStatusManager appStatusMgr)
        {
            _errorMgr = errorMgr;
            _appStatusMgr = appStatusMgr;
        }

        public IObservable<Unit> PublishError(BasicErrorReport report)
        {
            return Rxn.Create(() =>
            {
                _errorMgr.InsertError(report);
            });
        }

        public IObservable<Unit> DeleteError(long id)
        {
            "Delete error not implemented".LogDebug();
            return new Unit().ToObservable();
        }

        public IObservable<RxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, params object[] meta)
        {
            return _appStatusMgr.UpdateSystemStatusWithMeta(".", status, meta);
        }

        public IObservable<Unit> PublishLog(Stream zippedLog)
        {
            return Rxn.Create(() =>
            {
                using (var contents = ZipFile.Read(zippedLog))
                {
                    contents.ExtractAll(Path.Combine("appstatus", "logs", $"{Guid.NewGuid()}.zip"));
                }
            });
        }
    }

}
