using System;
using System.Diagnostics;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.Windows
{
    public static class RxnPlatformExtensions
    {
        public static IRxnApp UseWindowsAddons(this IRxnApp app)
        {
            Observable.FromEventPattern<UnhandledExceptionEventHandler, UnhandledExceptionEventArgs>(
                    h => AppDomain.CurrentDomain.UnhandledException += h,
                    h => AppDomain.CurrentDomain.UnhandledException -= h)
                .Subscribe(GeneralLogging.Log, (e) => GeneralLogging.Log.OnError(e.EventArgs.ExceptionObject as Exception))
                .DisposedBy(GeneralLogging.Log);

            PlatformHelper.CallingTypeNameImpl = () =>
            {
                var callerMethod = new StackFrame(3).GetMethod();
                return callerMethod == null ? "Unknown" : callerMethod.Name;
            };

            app.Definition.UpdateWith(cb =>
            {
                cb.Includes<WindowsModule>();
            });

            return app;
        }
    }
}
