using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using Autofac;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.Windows
{
    public class WindowsModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            Observable.FromEventPattern<UnhandledExceptionEventHandler, UnhandledExceptionEventArgs>(
                    h => AppDomain.CurrentDomain.UnhandledException += h,
                    h => AppDomain.CurrentDomain.UnhandledException -= h)
                .Subscribe(ReportStatus.Log, (e) => ReportStatus.Log.OnError(e.EventArgs.ExceptionObject as Exception))
                .DisposedBy(ReportStatus.Log);

            PlatformHelper.CallingTypeNameImpl = () =>
            {
                var callerMethod = new StackFrame(3).GetMethod();
                return callerMethod == null ? "Unknown" : callerMethod.Name;
            };

            return lifecycle
                .CreatesOncePerApp<WindowsSystemInformationService>()
                .CreatesOncePerApp<WindowsSystemServices>();
        }
    }
}
