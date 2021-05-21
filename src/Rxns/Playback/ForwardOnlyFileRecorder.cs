using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using System.Text;
using System.Threading;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    /// <summary>
    /// An append only stream recorder
    /// </summary>
    public class ForwardOnlyFileRecorder : IRecording
    {
        private readonly IStringCodec _codec;
        private readonly Action<ForwardOnlyFileRecorder> _onStop;
        private readonly StreamWriter _writer;
        private readonly DateTime _startedAt;
        private readonly Func<DateTime> _now;
        private long _isRecording;
        private IDisposable _recordingWatcher;

        public TimeSpan Duration { get; private set; }


        public ForwardOnlyFileRecorder(Stream recordTo, IStringCodec codec, Action<ForwardOnlyFileRecorder> onStop = null, TimeSpan? offSet = null, Func<DateTime> currentTime = null, IObservable<bool> shouldRecord = null)
        {
            _codec = codec;
            _onStop = onStop;
            _now = currentTime ?? SystemClock.Now;
            _writer = new StreamWriter(recordTo, Encoding.UTF8, 4069, false);
            _recordingWatcher = shouldRecord.Do(should => Interlocked.Exchange(ref _isRecording, should ? 1 : 0)).Until();

            //special chars are written to the end of a file, im having no luck appending too it
            //using seeking, beause i dont know where the special chars are. using file.Append works
            //var crString = Environment.NewLine;
            //var crBytes = Encoding.UTF8.GetBytes(crString);
            //recordTo.Seek(crBytes.Length, SeekOrigin.End);
            _startedAt = offSet != null ? _now().Add(offSet.Value) : _now();

            Duration = TimeSpan.Zero;
        }

        public void Record(IRxn rxn, TimeSpan? sinceBeginning = null)
        {
            if (Interlocked.Read(ref _isRecording) == 0) return;

            sinceBeginning = sinceBeginning ?? _now() - _startedAt;
            Duration = sinceBeginning.Value;

            _writer.Write("{0}{1}", _codec.ToString(new CapturedRxn(sinceBeginning.Value, rxn)), _codec.Delimiter);
        }

        public void FlushNow()
        {
            _writer.Flush();
        }

        public void Dispose()
        {
            Duration = _now() - _startedAt;
            if (_onStop != null) _onStop(this);
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
