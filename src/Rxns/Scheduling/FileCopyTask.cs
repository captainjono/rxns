using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Copies a set of files from one directory to another
    /// </summary>
    public partial class FileCopyTask : SchedulableTask
    {
        public override string ReporterName
        {
            get { return String.Format("FileCopy<{0}>", Files.Count); }
        }

        /// <summary>
        /// NOTE:
        /// Other public variables for this class are contained in .Model.cs
        /// </summary>

        private IFileSystemService _fileSystemService;

        private readonly List<string> _failedFiles = new List<string>();

        public FileCopyTask(IFileSystemService fileSystemService)
        {
            _fileSystemService = fileSystemService;

            Files = new List<string>();
        }

        private IObservable<IEnumerable<string>> GetFileListAsBuffered(ref ExecutionState state)
        {
            return GetFileList(ref state).Buffer(Threads);
        }

        public virtual IObservable<string> GetFileList(ref ExecutionState state)
        {
            return Files.ToObservable();
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Observable.Create<ExecutionState>(o =>
            {
                int maxTries = RetryCount;

                //setup the metrics properties
                Result.Name = ReporterName;
                //intial value of 0 file copied. 
                //this is updated by Success()
                Result.Value = 0;
                //other state variables
                _failedFiles.Clear();
                BindToState(state);

                //now get a list of files to copy
                return GetFileListAsBuffered(ref state).SelectMany(files =>
                {

                    //perform the copy
                    return files.ToObservable(NewThreadScheduler.Default).Do(f => CopyFile(f));
                })
                .DoWhile(() => --maxTries > 0 && _failedFiles.Count > 0)
                .Do(_ =>
                {
                    if (_failedFiles.Any() && ErrorOnNotFound)
                    {
                        o.OnError(new Exception(String.Format("The following files failed to copy: {0}", _failedFiles.ToStringEach())));
                    }
                })
                .Subscribe(_ => { },
                error =>
                {
                    OnError(error);
                    o.OnError(error);
                },
                onCompleted: () =>
                {
                    o.OnNext(state);
                    o.OnCompleted();
                });

            }).ToTask();
        }

        private void CopyFile(string fileToCopy)
        {
            //we need to trim the leading '\' from a path, because this will cause 
            //path.combine to ignore the source, unless of course this is the desired behaviour
            var file = fileToCopy.TrimStart('\\');
            var fileName = _fileSystemService.GetFilePart(file);

            var source = _fileSystemService.PathCombine(Source, file);
            string destination;

            //if we have a mapping value, rename the file here
            if (FileMap != null && FileMap.ContainsKey(fileName))
            {
                destination = _fileSystemService.PathCombine(Destination, PreserveDirectoryStructure ? file.Replace(fileName, FileMap[fileName]) : FileMap[fileName]);
            }
            else
            {
                destination = _fileSystemService.PathCombine(Destination, PreserveDirectoryStructure ? file : fileName);
            }

            try
            {

                if (!_fileSystemService.ExistsDirectory(Destination))
                {
                    _fileSystemService.CreateDirectory(Destination);
                }

                //verify the directories exist
                //assume any misconfiguration is expected to
                //be a failure due to consistency risks
                if (!_fileSystemService.ExistsFile(source))
                {
                    var msg = String.Format("Nothing to copy as source file '{0}' does not exist", source);
                    if (ErrorOnNotFound)
                    {
                        OnError(msg);
                        Fail(file);
                    }
                    else
                        OnWarning(msg);

                    return;
                }

                //dont copy if there is a destination file that is the same as the source
                if (_fileSystemService.ExistsFile(destination))
                    if (_fileSystemService.IsNewer(source, destination))
                    {
                        OnVerbose("Deleting '{0}' because the source is newer", destination);
                        _fileSystemService.DeleteFile(destination);
                    }
                    else
                    {
                        OnVerbose("Skipping '{0}' because the destination file already exists", file);
                        return;
                    }

                //do the copy
                _fileSystemService.Copy(source, destination, Buffer);

                //do any validation
                if (ValidateCopy)
                {
                    if (_fileSystemService.AreEqual(source, destination))
                        OnInformation("Files copy verified for '{0}'", file);
                    else
                    {
                        OnWarning("Validation failed, deleting '{0}' because it is corrupt", file);
                        _fileSystemService.DeleteFile(destination);

                        //only re-add the file if its not already in the list
                        Fail(file);

                        return;
                    }
                }

                Success(file);
            }
            catch (Exception e)
            {
                OnWarning(String.Format("{0}\r\n{1}", e.Message, e.StackTrace));
                Fail(file);
            }
        }

        private void Success(string file)
        {
            OnInformation("'{0}' copied successfully", file);

            //increment result
            Result.Value = ((int)Result.Value) + 1;

            //copy was successful
            if (_failedFiles.Contains(file))
            {
                _failedFiles.Remove(file);
            }
        }

        private void Fail(string file)
        {
            OnWarning("'{0}' failed to copy", file);

            if (!_failedFiles.Contains(file))
                _failedFiles.Add(file);
        }

        protected override void BindToState(ExecutionState state)
        {
            Source = String.IsNullOrEmpty(Source) ? BindToState("{Source}", state, "").ToString() :
                                                    BindToState(Source, state).ToString();

            Destination = String.IsNullOrEmpty(Destination) ? BindToState("{Destination}", state).ToString() :
                                                              BindToState(Destination, state).ToString();
            base.BindToState(state);
        }
    }
}
