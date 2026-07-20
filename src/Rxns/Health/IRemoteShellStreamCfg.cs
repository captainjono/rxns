using System;

namespace Rxns.Health
{
    /// <summary>
    /// Tunes how a long-lived shell session streams output back to subscribers.
    /// Without this, a chatty command (`dmesg`, `find /`, `grep -r foo`)
    /// accumulates unboundedly on the worker and then ships in one truncated
    /// dump after the marker arrives.
    ///
    /// The shell flushes a partial result whenever EITHER threshold is hit:
    /// • <see cref="BufferDuration"/> elapsed since the first buffered line
    /// • <see cref="MaxLinesPerPartial"/> lines accumulated
    /// • <see cref="MaxBytesPerPartial"/> bytes accumulated
    /// </summary>
    public interface IRemoteShellStreamCfg
    {
        TimeSpan BufferDuration { get; }
        int MaxLinesPerPartial { get; }
        int MaxBytesPerPartial { get; }
    }

    public class DefaultRemoteShellStreamCfg : IRemoteShellStreamCfg
    {
        public TimeSpan BufferDuration { get; } = TimeSpan.FromSeconds(1);
        public int MaxLinesPerPartial { get; } = 20;
        public int MaxBytesPerPartial { get; } = 16 * 1024;
    }
}
