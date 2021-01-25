using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    public class FileSystemTaskProvider : DeseriliseTaskProvider
    {
        /// <summary>
        /// The serilised definition of the tasks that this provider returns
        /// </summary>
        public string ConfigurationFile { get; set; }
        /// <summary>
        /// The location of the configuraiton file
        /// </summary>
        public string Directory { get; set; }

        private readonly IFileSystemService _fileSystem;
        private IDisposable _configurationMonitor;
        
        public FileSystemTaskProvider(IFileSystemService fileSystem, string filename = "tasks/tasks.json")
        {
            _fileSystem = fileSystem;
            ConfigurationFile = filename;
            Directory = "./"; //Environment.CurrentThre
        }

        public override IObservable<ISchedulableTaskGroup[]> GetTasks()
        {
            if (_configurationMonitor == null)
            {
                OnVerbose("Monitoring '{0}' for tasks", Path.Combine(Directory, ConfigurationFile));

                _configurationMonitor = _fileSystem.OnUpdate(Directory, ConfigurationFile).Subscribe(this, _ => ReadAndCacheConfiguration()).DisposedBy(this);  //Files.WatchForChanges(Directory, ConfigurationFile, ReadAndCacheConfiguration);
            }

            return base.GetTasks();
        }

        public void ReadAndCacheConfiguration()
        {
            var path = Directory.IsNullOrWhitespace() ? ConfigurationFile : Path.Combine(Directory, ConfigurationFile);

            if (_fileSystem.ExistsFile(path))
            {
                SerialisedTasks = new StreamReader(_fileSystem.GetReadableFile(path)).ReadToEnd();
            }
        }
    }
}
