using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    public partial class DeleteTask : SchedulableTask
    {
        private readonly IFileSystemService _fileSystem;

        public override string ReporterName
        {
            get { return String.Format("Delete<{0}>", Files.Length() + Directories.Length()); }
        }

        public DeleteTask(IFileSystemService filesSystem)
        {
            _fileSystem = filesSystem;
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Observable.Create<ExecutionState>(o =>
            {
                try
                {
                    if (Files.AnyItems())
                    {
                        OnInformation("Deleteing {0} files", Files.Length);

                        foreach (var file in Files)
                            _fileSystem.DeleteFile(file);
                    }

                    if (Directories.AnyItems())
                    {
                        OnInformation("Deleteing {0} directories", Directories.Length);

                        foreach (var dir in Directories)
                            _fileSystem.DeleteDirectory(dir);
                    }

                    Result.SetAsSuccess(Files.Length() + Directories.Length());
                }
                catch (Exception e)
                {
                    Result.SetAsFailure(e);
                    o.OnError(e);
                }

                o.OnNext(state);
                o.OnCompleted();

                return Disposable.Empty;
            }).ToTask();
        }
    }
}
