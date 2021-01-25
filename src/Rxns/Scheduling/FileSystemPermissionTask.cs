using System;
using System.ComponentModel;
using System.Reactive.Threading.Tasks;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    public enum FileSystemPermission
    {
        FullControl
    }
    
    public partial class FileSystemPermissionTask : SchedulableTask
    {
        private readonly IFileSystemService _fileSystem;

        [DataMember]
        [DefaultValue(true)]
        public bool WarnOnFailures { get; set; }

        public FileSystemPermissionTask(IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem;
            WarnOnFailures = true;
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Rxn.Create(() =>
            {
                try
                {
                    _fileSystem.SetFullAccessPermissions(Path, Usernames);
                }
                catch (Exception e)
                {
                    if (WarnOnFailures)
                        OnWarning("Could not set permissions because {0}", e);
                    else
                        throw;
                }
                return state;
            }).ToTask();
        }
    }
}
