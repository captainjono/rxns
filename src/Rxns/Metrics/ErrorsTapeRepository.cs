using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Collections;
using Rxns.Health.AppStatus;
using Rxns.Logging;
using Rxns.Playback;

namespace Rxns.Metrics
{
    /// <summary>
    /// this class currently is a noop
    /// </summary>
    public class ErrorsTapeRepository : IErrorRepository, IDisposable
    {
        private readonly ITapeRepository _repoFactory;

        public ErrorsTapeRepository(ITapeRepository repoFactory)
        {
            _repoFactory = repoFactory;
        }

        public string AddError(BasicErrorReport report)
        {
            var reportId = Guid.NewGuid().ToString();
            report.ErrorId = reportId;
            
            AddErrorMeta(reportId, report.History.Select(h => new SystemLogMeta()
            {
                Reporter = h.Reporter,
                Level = h.Level.ToString(),
                Message = h.Message,
                Timestamp = h.Timestamp,
            }).ToArray());


            var repo = _repoFactory.GetOrCreate($"repos/error_{reportId}");
            report.History = null;

            using (var writer = repo.Source.StartRecording())
            {
                writer.Record(report);
                writer.FlushNow();
            }

            return reportId;
        }

        public void AddErrorMeta(string errorReportId, SystemLogMeta[] meta)
        {
            var repo = _repoFactory.GetOrCreate($"repos/meta_{errorReportId}");

            using (var writer = repo.Source.StartRecording())
            {
                foreach (var record in meta)
                {
                    writer.Record(record);
                    writer.FlushNow();
                }
            }
        }

        public void DeleteError(long errorId)
        {

        }

        public void DeleteMetric(long metricId)
        {

        }

        public IObservable<SystemLogMeta> GetErrorMeta(string errorId, Func<SystemLogMeta, bool> where = null)
        {
            return _repoFactory.GetOrCreate($"repos/meta_{errorId}").Source.Contents.Select(c => c.Recorded as SystemLogMeta);
        }

        public IEnumerable<SystemErrors> GetErrors(Func<SystemErrors, bool> where = null, Page pageWith = null)
        {
            var all = _repoFactory.GetAll("repos", "error_*").ToArray();

            foreach (var ee in all)
            {
                var ce = ee.Source.Contents.Take(1).WaitR() as CapturedRxn;
                if (ce == null) continue;

                var e = ce.Recorded as BasicErrorReport;
                yield return new SystemErrors()
                {
                    Error = e.Error,
                    Actioned = false,
                    ErrorId = e.ErrorId,
                    StackTrace = e.StackTrace,
                    System = e.System,
                    Tenant = e.Tenant
                };
            }
        }

        public void Dispose()
        {
        }
    }
}
