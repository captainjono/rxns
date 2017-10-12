using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Playback
{
    public class CapturedEventTapeSource : IFileTapeSource
    {
        private readonly IStringCodec _codec;
        private readonly bool _skipErrors;
        public TimeSpan Duration { get; private set; }
        public IFileMeta File { get; private set; }
        public IEnumerable<ICapturedRxn> Contents { get { return ReadTo(); } }

        public CapturedEventTapeSource(TimeSpan duration, IFileMeta file, IStringCodec codec)
        {
            _codec = codec;
            _skipErrors = codec.SkipErrors;

            File = file;
            Duration = duration;
        }

        public IRecording StartRecording()
        {//is there something here with the access voilation? i should unit test this all to find out. use a different IFileMeta? IReadWriteFileMeta to stop the confusion..?
            return new CapturedEventRecorder(File.Contents, _codec, onStop: r => Duration = r.Duration);
        }

        private IEnumerable<ICapturedRxn> ReadTo(Func<IRxn, bool> selector = null)
        {
            selector = selector ?? new Func<IRxn, bool>(_ => false);

            using (var contents = new StreamReader(File.Contents, Encoding.UTF8, false, 2048, false))
            {
                //polled memory so we dont allocate on each iteration
                var buffer = new char[2048];
                var i = 0;
                var nextEvent = new StringBuilder();
                var charsRead = buffer.Length;

                while (charsRead == buffer.Length)
                {
                    charsRead = contents.Read(buffer, 0, buffer.Length);

                    for (i = 0; i < charsRead; i++)
                        if (buffer[i] != _codec.Delimiter)
                            nextEvent.Append(buffer[i]);
                        else
                        {
                            ICapturedRxn next = null;
                            try
                            {
                                next = (ICapturedRxn)_codec.FromString<CapturedRxn>(nextEvent.ToString());
                            }
                            catch (Exception e)
                            {
                                if (_skipErrors)
                                    GeneralLogging.Log.OnWarning(File.Name, e.ToString());
                                else
                                    throw;
                            }

                            if (next != null)
                            {
                                if (selector(next.Recorded)) break;
                                yield return next;
                            }
                            nextEvent.Clear();
                        }
                }
            }
        }

        public void Rewind()
        {
            if (File.Contents.CanSeek) File.Contents.Seek(0, SeekOrigin.Begin);
        }

        public void SkipTo(Func<IRxn, bool> selector = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
