using System;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Rxns.Collections;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class UnzipTask
    {
        [DataMember]
        [DefaultValue(null)]
        public string File { get; set; }

        [DataMember]
        [DefaultValue(null)]
        public string Directory { get; set; }

        [DataMember]
        [DefaultValue(true)]
        public bool PreserveStructure { get; set; }
    }

    public partial class UnzipTask : SchedulableTask, ITask
    {
        private readonly IZipService _zipService;
        private readonly IFileSystemService _fileSystem;

        public UnzipTask(IZipService zipService, IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem;
            _zipService = zipService;
        }

        public IObservable<Unit> Execute()
        {
            return _zipService.GetFiles(File).Do(file =>
            {
                try
                {
                    var path = _fileSystem.PathCombine(Directory, PreserveStructure ? file.Fullname : file.Name);
                    OnInformation("> {0}", path);

                    using (var fileContents = _fileSystem.CreateWriteableFile(path))
                    {
                        file.Contents.CopyTo(fileContents);
                    }
                }
                catch (IOException e
                ) //this is simply for integration testing, specifically the install test as i cant unload the dll from the domain after it finishes
                {
                    if (e.Message.Contains("another process"))
                    {
                        OnWarning("[{0}]{1}", file.Name, e.Message);
                        return;
                    }

                    throw;
                }
            })
            .LastOrDefaultAsync()
            .Select(_ => new Unit());

        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Execute().Select(_ => state).ToTask();
        }
    }
}
