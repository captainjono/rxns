using System;
using System.Linq;
using System.Text.RegularExpressions;
using RxnsDemo.AzureB2C.RxnApps;

namespace RxnsDemo.AzureB2C
{
    public enum ImportProgress
    {
        Queued = 0,
        Processing,
        Failure,
        PartialSuccess,
        Success,
        NothingToProcess
    }

    public interface IImportResult
    {
        string[] Log { get; }
    }

    public interface IImportFailureResult : IImportResult
    {
        string Error { get; } //todo: convert this to an ExceptionMeta object which has the error, stacktrace and message, as opposed to the exception which causes issues with serilisation!!
    }

    public class ProgressOfImport
    {
        private int? _resultCount;
        public string Source { get; private set; }
        public DateTime Started { get; private set; }
        public DateTime LastUpdated { get; private set; }
        public ImportProgress Progress { get; private set; }
        public string Tenant { get; private set; }
        public UserImportSuccessResult[] Successes { get; private set; }
        public ImportFailureResult[] Failures { get; private set; }
        public string Error { get; private set; }

        private ProgressOfImport()
        {
            Successes = new UserImportSuccessResult[] { };
            Failures = new ImportFailureResult[] { };
        }

        public ProgressOfImport(string tenant, string source)
            : this()
        {
            Source = source;
            Progress = ImportProgress.Queued;
            Tenant = tenant;
            Started = LastUpdated = DateTime.Now;
        }

        public ProgressOfImport MarkAsSuccess(UserImportSuccessResult success)
        {
            if (Progress != ImportProgress.Processing) throw new Exception("Cannot add data to a report which is not processing");

            //need to do it this was otherwise if this report is serilised during execution and i used list.Add it would corrupt the enumerator
            Successes = Successes.Concat(new[] { success }).ToArray();
            LastUpdated = DateTime.Now;

            return this;
        }

        public ProgressOfImport MarkAsFailure(ImportFailureResult failure)
        {
            if (Progress != ImportProgress.Processing) throw new Exception("Cannot add data to a report which is not processing");

            Failures = Failures.Concat(new[] { failure }).ToArray();
            Error = failure.Error.deCapitalise().AsHumanReadable();
            LastUpdated = DateTime.Now;

            return this;
        }

        public ProgressOfImport InProgress()
        {
            Progress = ImportProgress.Processing;
            LastUpdated = DateTime.Now;

            return this;
        }

        public ProgressOfImport Complete()
        {
            if (!Failures.Any() && !Successes.Any()) Progress = ImportProgress.NothingToProcess;
            else if (Failures.Any() && Successes.Any()) Progress = ImportProgress.PartialSuccess;
            else if (Failures.Any()) Progress = ImportProgress.Failure;
            else if (Successes.Any()) Progress = ImportProgress.Success;

            LastUpdated = DateTime.Now;

            return this;
        }

        public bool IsComplete()
        {
            return _resultCount >= Failures.Length + Successes.Length;
        }

        public void Expect(int resultCount)
        {
            _resultCount = resultCount;
        }
    }

    public static class StringExtenions
    {
        public static string AsHumanReadable(this string message)
        {
            return Regex.Replace(message, @"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])", " $1$2");

        }

        public static string deCapitalise(this string message)
        {
            return message.IsNullOrWhitespace()
                ? ""
                : "{0}{1}".FormatWith(message[0].ToString().ToLower(), message.Substring(1, message.Length - 1));
        }
    }
}
