﻿using System;
using System.IO;
using System.Reactive.PlatformServices;
using System.Text;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    /// <summary>
    /// An append only stream recorder
    /// </summary>
    public class CapturedEventRecorder : IRecording
    {
        private readonly IStringCodec _codec;
        private readonly Action<CapturedEventRecorder> _onStop;
        private readonly StreamWriter _writer;
        private readonly DateTime _startedAt;
        private readonly Func<DateTime> _now;

        public TimeSpan Duration { get; private set; }

        public CapturedEventRecorder(Stream recordTo, IStringCodec codec, Action<CapturedEventRecorder> onStop = null, TimeSpan? offSet = null, Func<DateTime> currentTime = null)
        {
            _codec = codec;
            _onStop = onStop;
            _now = currentTime ?? SystemClock.Now;
            _writer = new StreamWriter(recordTo, Encoding.UTF8, 4069, false);

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