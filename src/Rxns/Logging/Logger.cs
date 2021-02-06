using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Rxns.Scheduling;

namespace Rxns.Logging
{
    public static class Logger
    {
        public static Subject<string> OnDebug = new Subject<string>();

        static Logger()
        {
            OnDebug.Do(msg =>
            {
                if (Debugger.IsAttached)
                {
                    Debug.WriteLine(msg);
                }
                else
                {
                    Console.WriteLine(msg);
                }
            }).Until();
        }
        public static string LogDebug(this string toLog)
        {
            OnDebug.OnNext($"[{Thread.CurrentThread.ManagedThreadId}][DBG] {toLog}");
            return toLog;
        }

        public static void OneThreadAtATime(Action toRun, object locker)
        {
            lock (locker)
            {
                toRun();
            }
        }

        public static string LogDebug(this string s, object id)
        {
            $"[{id}] {s}".LogDebug();

            return s;
        }

        public static Action<long, long> CreateFileProgressLogger(string fileName, Action<string> downloadProgress = null)
        {
            var startTime = DateTime.Now;
            var lastPoll = startTime;
            var logProgress = new Subject<Tuple<long, long>>();
            long lastTotal = 0;

            logProgress.Sample(TimeSpan.FromMinutes(0.1)).Do(p =>
            {
                var totalDownloaded = p.Item1;
                var totalToDownload = p.Item2;
                var leftToDownload = totalToDownload - totalDownloaded;
                var justDownloaded = totalDownloaded - lastTotal;
                var secondsJustDownloadedTook = DateTime.Now - lastPoll;
                var downloadSpeedPerSec = justDownloaded / secondsJustDownloadedTook.TotalSeconds;
                var remainingTimeToDownload = TimeSpan.FromSeconds(leftToDownload / downloadSpeedPerSec);

                lastTotal = p.Item1;
                lastPoll = DateTime.Now;
                var progress = (float)lastTotal / p.Item2;

                downloadProgress?.Invoke($"{progress:P} (Remaining: {remainingTimeToDownload:c} {ByteSizeToHumanString(downloadSpeedPerSec)} p/sec)");
            })
            .Subscribe(); //todo: return this

            return (c, total) => logProgress.OnNext(new Tuple<long, long>(c, total));
        }

        public static string ByteSizeToHumanString(this double size, bool useBase1000 = false)
        {
            var sizes = new[] { "B", "kB", "MB", "GB", "TB" };
            int order = 0;
            double sizeFloat = size;
            var kbase = useBase1000 ? 1000 : 1024;
            while (sizeFloat >= kbase && order + 1 < sizes.Length)
            {
                order++;
                sizeFloat = sizeFloat / kbase;
            }

            string result = String.Format("{0:0.##} {1}", sizeFloat, sizes[order]);
            return result;
        }

    }
}
