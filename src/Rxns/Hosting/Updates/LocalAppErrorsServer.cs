using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Ionic.Zip;
using Rxns.Cloud;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public class LocalAppStatusServer : IAppStatusServiceClient
    {
        protected readonly IAppErrorManager _errorMgr;
        protected readonly IAppStatusManager _appStatusMgr;
        private readonly IRxnManager<IRxn> _rxnmanager;

        public LocalAppStatusServer(IAppErrorManager errorMgr, IAppStatusManager appStatusMgr, IRxnManager<IRxn> rxnmanager)
        {
            _errorMgr = errorMgr;
            _appStatusMgr = appStatusMgr;
            _rxnmanager = rxnmanager;
        }

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return events.ToObservableSequence().SelectMany(e => _rxnmanager.Publish(e)).LastOrDefaultAsync();
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

        public virtual IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            return _appStatusMgr.UpdateSystemStatusWithMeta(".", status, meta);
        }

        public IObservable<string> PublishLog(Stream zippedLog)
        {
            var fileName = $"{Guid.NewGuid()}.zip";
            return Rxn.Create(() =>
            {
                using (var contents = ZipFile.Read(zippedLog))
                {
                    contents.ExtractAll(Path.Combine("appstatus", "logs", fileName));
                }

                return fileName;
            });
        }
    }

}
