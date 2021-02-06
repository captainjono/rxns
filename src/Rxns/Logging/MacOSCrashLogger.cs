using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    //todo: att lldb support
    //improve logger to support core-dumps / macos `lldb debugger` auto-attachment
    //it would probably be some kind of decorator to the Run*() command, that if it detects a crash occoured
    //to re-run inside of the debuggeR? 
    //or would u just run in the debugger, all the time? probably wouldnt, because ur app would need to have the get-task-allow perm
    // https://stackoverflow.com/questions/26812047/scripting-lldb-to-obtain-a-stack-trace-after-a-crash
    public class MacOSCrashLogger : IContainerPostBuildService, IDisposable
    {
        private readonly IRxnAppInfo _appInfo;
        private IDisposable _watcher = Disposable.Empty;

        public MacOSCrashLogger(IRxnAppInfo system)
        {
            _appInfo = system;
        }
        
        public void Run(IReportStatus logger, IResolveTypes container)
        {
            if (!Directory.Exists("/Library/Logs/DiagnosticReports/"))
            {
                "Cannot detect logs directory, disabling".LogDebug();
                return;
            }
            
            _watcher = TimeSpan.FromSeconds(10).Then().Do(_ => MonitorForCrashesOnMacos(_appInfo.Name, container.Resolve<ReporterErrorLogger>())).Until();
        }

        private static void MonitorForCrashesOnMacos(string programName, ReporterErrorLogger logger)
        {
            var allErrors = Directory.GetFiles($"/Library/Logs/DiagnosticReports/", $"{programName}*.*");
            allErrors.ForEach(e =>
            {
                $"Detected crash report: {e}".LogDebug(programName);

                //log the actual crash, then delete it
                logger.LogError(new LogMessage<Exception>()
                {
                    Reporter = programName,
                    Level = LogLevel.Error,
                    Message = new Exception(File.ReadAllText(e)),
                    Timestamp = DateTime.Now
                });
                //File.Delete(e);
            });
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
    
}