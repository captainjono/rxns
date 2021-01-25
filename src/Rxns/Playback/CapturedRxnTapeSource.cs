using System;   
using System.IO;
using System.IO.Pipes;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Playback
{
    public class RxnsStream
    {
        //public static IObservable<T> ReadStream<T>(PipeStream rxnSource, IStringCodec _codec, bool _skipErrors = false)
        //    where T : class, IRxn
        //{
        //    return Rxn.Create<T>(o =>
        //        {
        //            var shouldRead = true;
        //            var nextEvent = new StringBuilder();
        //            var buffer = new byte[2048];
        //            //var rxnsStream = new StreamReader(rxnSource, Encoding.UTF8, false, 2048, false);

        //            //polled memory so we dont allocate on each iteration
        //            Func<IObservable<T>> read = () => rxnSource.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None).ToObservable()
        //                .Select(charsRead => ConvertMsgToRxn<T>(nextEvent, charsRead, buffer, _codec, _skipErrors));

        //            return Rxn.DfrCreate(read)
        //                .Where(v => v != default(T))
        //                .Do(o.OnNext)
        //                .Repeat()
        //                .Subscribe();
        //        })
        //        .Publish()
        //        .RefCount();
        //}

        public static IObservable<T> ReadStream<T>(Stream contents, IStringCodec _codec, bool _skipErrors = false) where T : class, IRxn
        {
            return Rxn.Create<T>(o =>
            {
                var rxnsStream = new StreamReader(contents, Encoding.UTF8, false, 2048, false);

                {
                    //polled memory so we dont allocate on each iteration
                    var buffer = new char[2048];
                    var i = 0;
                    var nextEvent = new StringBuilder();
                    var charsRead = buffer.Length;

                    while (true)
                    {
                        charsRead = rxnsStream.Read(buffer, 0, buffer.Length);

                        for (i = 0; i < charsRead; i++)
                            if (buffer[i] != _codec.Delimiter)
                                nextEvent.Append(buffer[i]);
                            else
                            {
                                T next = null;
                                try
                                {
                                    next = ConvertMsgToRxn<T>(nextEvent, charsRead, buffer, _codec, _skipErrors);
                                }
                                catch (Exception e)
                                {
                                    if (_skipErrors)
                                        ReportStatus.Log.OnWarning("ReadStream", e.Message);
                                    else
                                        throw;
                                }

                                if (next != null)
                                {
                                    o.OnNext(next);
                                }
                                nextEvent.Clear();
                            }
                    }

                    o.OnCompleted();
                    return contents;
                }
            }).Publish()
                .RefCount();

          
        }

        public static IObservable<T> ReadPipeStream<T>(PipeStream rxnSource, IStringCodec _codec, bool _skipErrors = false) where T : class, IRxn
        {
            return Rxn.Create<T>(o =>
            {
                var shouldRead = true;
                var nextEvent = new StringBuilder();
                var buffer = new byte[2048];
                var msgChunk = String.Empty;

                //polled memory so we dont allocate on each iteration

                Func<IObservable<T>> read = () =>
                {
                    return Rxn.Create<T>(() =>
                    {
                        do
                        {                                
                            var amount = rxnSource.Read(buffer, 0, buffer.Length);
                            msgChunk = Encoding.UTF8.GetString(buffer, 0, amount);
                            nextEvent.Append(msgChunk);
                        } while (!rxnSource.IsMessageComplete);

                        var parsed = nextEvent.ToString();
                        nextEvent.Clear();
                        return  ConvertMsgToRxn<T>(parsed, _codec, _skipErrors);
                    });
                };

                return Rxn.DfrCreate(read)
                    .Where(v => v != default(T))
                    .Do(o.OnNext)
                    .Repeat()
                    .Until(o.OnError);
            })
            .Publish()
            .RefCount();
        }

        //public static IObservable<T> ReadStream<T>(Stream rxnSource, IStringCodec _codec, bool _skipErrors = false) where T : class, IRxn
        //{
        //    return Rxn.Create<T>(o =>
        //    {
        //        var shouldRead = true;
        //        var nextEvent = new StringBuilder();
        //        var buffer = new char[2048];

        //        var rxnsStream = new StreamReader(rxnSource, Encoding.UTF8, false, 2048, false);

        //        //polled memory so we dont allocate on each iteration

        //        Func<IObservable<T>> read = () => rxnsStream.ReadAsync(buffer, 0, buffer.Length).ToObservable()
        //            .Select(charsRead => ConvertMsgToRxn<T>(nextEvent, charsRead, buffer, _codec, _skipErrors));

        //        return Rxn.DfrCreate(read)
        //            .Where(v => v != default(T))
        //            .Do(o.OnNext)
        //            .Repeat()
        //            .Subscribe();
        //    })
        //    .Publish()
        //    .RefCount();
        //}

        private static T ConvertMsgToRxn<T>(StringBuilder nextEvent, long charsRead, char[] buffer, IStringCodec codec, bool skipErrors)
        {
            
                    T next = default(T);
                    try
                    {
                        //something is not writing the event correctly, invalid json is being rturned
                        next = (T)codec.FromString<T>(nextEvent.ToString());
                    }
                    catch (Exception e)
                    {
                        if (skipErrors)
                            ReportStatus.Log.OnWarning(e.ToString());
                        else
                            throw;
                    }
                    nextEvent.Clear();

                    if (next != null)
                    {
                        return next;
                }

            return default(T);
        }

        private static T ConvertMsgToRxn<T>(string nextEvent, IStringCodec codec, bool skipErrors)
        {
            T next = default(T);
            try
            {
                //something is not writing the event correctly, invalid json is being rturned
                return (T)codec.FromString<T>(nextEvent.TrimEnd(codec.Delimiter));
            }
            catch (Exception e)
            {
                if (skipErrors)
                    ReportStatus.Log.OnWarning(e.ToString());
                else
                    throw;
            }

            return default(T);
        }

        public static void WriteStream(Stream rxnDestination, IStringCodec codec, IRxn message)
        {
            var msg = Encoding.UTF8.GetBytes(ConvertRxnToMsg(message, codec));
            rxnDestination.Write(msg,0, msg.Length);
            rxnDestination.Flush();
        }

        private static string ConvertRxnToMsg(IRxn message, IStringCodec codec)
        {
            return $"{codec.ToString(message)}{codec.Delimiter}";
        }
    }

    public class CapturedRxnTapeSource : IFileTapeSource
    {
        private readonly IStringCodec _codec;
        private readonly bool _skipErrors;
        public TimeSpan Duration { get; private set; }
        public IFileMeta File { get; private set; }
        public IObservable<ICapturedRxn> Contents { get { return ReadTo(); } }

        public CapturedRxnTapeSource(TimeSpan duration, IFileMeta file, IStringCodec codec)
        {
            _codec = codec;
            _skipErrors = codec.SkipErrors;

            File = file;
            Duration = duration;
        }

        public IRecording StartRecording()
        {//is there something here with the access voilation? i should unit test this all to find out. use a different IFileMeta? IReadWriteFileMeta to stop the confusion..?
            var store = File.Contents;
            store.Seek(0, SeekOrigin.End);
            return new ForwardOnlyFileRecorder(store, _codec, onStop: r => Duration = r.Duration);
        }

        private IObservable<ICapturedRxn> ReadTo(Func<IRxn, bool> selector = null)
        {
            return Rxn.Create<ICapturedRxn>(o =>
            {
                selector = selector ?? new Func<IRxn, bool>(_ => false);

                return RxnsStream.ReadStream<CapturedRxn>(File.Contents, _codec, _skipErrors)
                    .Do(r =>
                    {
                        if (selector(r)) return;
                        o.OnNext(r);
                    })
                    .FinallyR(() =>
                    {
                        o.OnCompleted();
                    })
                    .Subscribe();
            });
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
