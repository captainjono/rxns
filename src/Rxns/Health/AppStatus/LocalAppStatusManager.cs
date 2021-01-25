using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Commanding;
using Rxns.DDD.Commanding;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.WebApi.AppStatus;

namespace Rxns.Health.AppStatus
{
    public class LocalAppStatusManager : ReportsStatus, IAppStatusManager, IRxnPublisher<IRxn>
    {
        private readonly ISystemStatusStore _systemStatus;
        private readonly IAppStatusStore _appStatus;
        private readonly IAppUpdateManager _updates;
        private Action<IRxn> _publish;
        private IDictionary<string, string> _keepUpToDateWithNewAppVersions = new ConcurrentDictionary<string, string>();


        public LocalAppStatusManager(ISystemStatusStore systemStatus, IAppStatusStore appStatus, IAppUpdateManager updates)
        {
            _appStatus = appStatus;
            _updates = updates;
            _systemStatus = systemStatus;
        }

        public dynamic GetSystemStatus()
        {
            return this.TryCatch(() =>
            {
                OnInformation("Getting system status");

                var all = GetStatus();

                if (all.AnyItems())
                {
                    //find all the tenants
                    return all.Distinct(new TenantOnlyStatusComparer()).Select(x => new
                    {
                        Tenant = x.Key.Tenant,
                        Systems = all.Keys.Where(k => k.Tenant == x.Key.Tenant)
                            .OrderBy(o => o.SystemName)
                            .Select(y => new
                            {
                                System = y,
                                Meta = all[y]
                            })
                    });
                }

                return all.ToLookup(t => t.Key.Tenant);
            });
        }

        private Dictionary<SystemStatusEvent, object[]> GetStatus()
        {
            return _systemStatus.GetAllSystemStatus().Wait();
        }

        private IObservable<bool> AddOrUpdateStatus(SystemStatusEvent status, params object[] meta)
        {
            OnInformation("Received status from '{0}\\{1}'", status.Tenant, status.SystemName, status.IpAddress);
            

            return _systemStatus.AddOrUpdate(status, meta)
                .Do(isNew =>
                {
                    if (isNew && IsSpareReactor(status))
                    {
                        _publish(new SpareReactorAvailible()
                        {
                            Route = status.GetRoute()
                        });
                    }
                });
        }

        private bool IsSpareReactor(SystemStatusEvent status)
        {
            return status.SystemName.Equals("sparereactor", StringComparison.OrdinalIgnoreCase);
        }

        public IObservable<bool> UpdateSystemStatus(SystemStatusEvent status, params object[] meta)
        {
            return this.TryCatch(() =>
            {
                return AddOrUpdateStatus(status, meta);
            });
        }

        public IObservable<RxnQuestion[]> UpdateSystemStatusWithMeta(string appRoute, SystemStatusEvent status, object meta)
        {
            return UpdateSystemStatus(status, meta)
                .SelectMany(wasAdded => _appStatus.FlushCommands(appRoute).ToObservable().ToArray().Concat(UpdateSystemCommandIfOutofDate(status)));
        }

        private IObservable<RxnQuestion[]> UpdateSystemCommandIfOutofDate(SystemStatusEvent status)
        {
            return _updates.AllUpdates(status.SystemName, 1).FirstAsync().Select(currentVersion =>
                {
                    if (!status.KeepUpToDate)
                    {
                        return null;
                    }

                    if (currentVersion.Equals(status.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return new RxnQuestion[] { new UpdateSystemCommand(status.SystemName, currentVersion, status.GetRoute())};
                })
                .Where(c => c != null);
        }

        public dynamic GetSystemLog()
        {
            return this.TryCatch(() =>
            {
                OnInformation("Getting system log");

                var log = _appStatus.GetLog() ?? new object[] { };

                return log.OrderByDescending(x => (x as SystemLogMeta)?.Timestamp);
            });
        }

        public IObservable<bool> UploadLogs(string tenantId, string systemName, IFileMeta file)
        {
            using (var zip = new StreamReader(file.Contents))
            {
                if (!file.Name.ToLower().Contains("zip"))
                    throw new ArgumentException("Update packages must be a zip file");

                OnInformation("Uploading log '{2}' for '{0}:{1}'", tenantId, systemName, file.Name);


                //_appStatus.SaveAsLog(tenantId, systemName, new FileMeta() { Fullname = file.Name, ContentType = "application/zip", LastWriteTime = DateTime.Now }, zip.BaseStream).Wait();

                OnVerbose("'{0}' received for log '{1}''", zip.BaseStream.Length.ToFileSize(), file.Name);
            }

            return true.ToObservable();
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }
    }
    public class TenantOnlyStatusComparer : IEqualityComparer<KeyValuePair<SystemStatusEvent, object[]>>
    {
        public bool Equals(KeyValuePair<SystemStatusEvent, object[]> x, KeyValuePair<SystemStatusEvent, object[]> y)
        {
            return x.Key.Tenant == y.Key.Tenant;
        }

        public int GetHashCode(KeyValuePair<SystemStatusEvent, object[]> obj)
        {
            return obj.Key.Tenant.GetHashCode();
        }
    }
}
