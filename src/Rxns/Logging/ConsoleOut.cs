using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace Rxns.Logging
{
    /// <summary>
    /// The one place console log lines are written, off the thread that produced them.
    ///
    /// <para>Console.Out is synchronized: every thread that writes takes the same lock, and holds
    /// it for the duration of the write. That is affordable when the handle is a terminal and
    /// ruinous when it is a file on a slow disk, which is exactly how a host started with
    /// RedirectStandardOutput runs. The threads queueing behind that lock are the same ones the
    /// web server needs to answer requests, so the symptom is not "logging is slow" - it is the
    /// process going unresponsive while its disk catches up.</para>
    ///
    /// <para>An arena measured emitting 4 MB/s of console output, and a run left a seven gigabyte
    /// log behind. Writing that synchronously from request threads cost about 1.8x on median
    /// response latency and 2.7x at p90 against the same work with output discarded.</para>
    ///
    /// <para>So lines are handed to a single writer running on its own event loop, and the caller
    /// returns immediately. Ordering is preserved because there is exactly one consumer.</para>
    /// </summary>
    public static class ConsoleOut
    {
        /// <summary>
        /// How many lines may be waiting before new ones are dropped.
        ///
        /// <para>Bounded on purpose. Unbounded, a producer that outruns the disk turns a logging
        /// problem into an out-of-memory one, and the backlog is unbounded precisely when the
        /// process is already in trouble. Dropping is the lesser failure, and it is counted rather
        /// than silent - <see cref="Dropped"/> says how many, so a gap in a log can be explained
        /// instead of puzzled over.</para>
        /// </summary>
        public static int MaxPending = 100_000;

        private static readonly Subject<string> _lines = new Subject<string>();
        private static int _pending;
        private static int _dropped;

        /// <summary>Lines discarded because the writer could not keep up.</summary>
        public static int Dropped => _dropped;

        /// <summary>Lines accepted and not yet written.</summary>
        public static int Pending => _pending;

        static ConsoleOut()
        {
            // One consumer, its own thread, so ordering holds and no producer ever blocks on it.
            _lines
                .ObserveOn(new EventLoopScheduler(t => new Thread(t)
                {
                    IsBackground = true,
                    Name = "rxns-console"
                }))
                .Subscribe(line =>
                {
                    try { Console.Out.WriteLine(line); }
                    catch { /* a broken stdout must not take the process with it */ }
                    finally { Interlocked.Decrement(ref _pending); }
                });

            // A background thread dies with the process, so the tail of the log - which is the part
            // worth having when something has gone wrong - would go with it.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Flush(TimeSpan.FromSeconds(5));
        }

        public static void WriteLine(string line)
        {
            if (Volatile.Read(ref _pending) >= MaxPending)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }

            Interlocked.Increment(ref _pending);
            _lines.OnNext(line);
        }

        /// <summary>
        /// Waits for what has been accepted to reach the console, up to a limit.
        ///
        /// <para>Bounded rather than unconditional: a flush that waits forever on a stalled disk
        /// hangs shutdown, which is worse than losing the last few lines.</para>
        /// </summary>
        public static void Flush(TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
        }
    }
}
