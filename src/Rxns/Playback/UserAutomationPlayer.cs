using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns.Playback
{
    public class UserAutomationPlayer : ITapeRecorder
    {
        private readonly IScheduler _playbackScheduler;

        public UserAutomationPlayer(IScheduler playbackScheduler = null)
        {
            _playbackScheduler = playbackScheduler ?? Scheduler.Default;
        }

        public IDisposable Record(ITapeStuff tape, IObservable<IRxn> stream)
        {
            var recording = tape.Source.StartRecording();

            return stream.Do(@e => recording.Record(@e)).FinallyR(() => recording.Dispose()).Subscribe();
        }

        public PlaybackStream Play(ITapeStuff tape, PlaybackSettings settings = null)
        {
            settings = settings ?? new PlaybackSettings();
            var playBackStream = new Subject<IRxn>();
            var position = new Subject<TimeSpan>();

            //todo: join buffers up using a gate to stop more actions buffering before they
            //have been played
            var sourceEventBuffer = Rxn.DfrCreate<ICapturedRxn>(o =>
            {
                return tape.Source
                            .Contents
                            .Buffer(settings.ActionBuffer)
                            .Do(events =>
                            {
                                @events.ForEach(e => o.OnNext(e));
                            })
                            .FinallyR(() =>
                            {
                                Debug.WriteLine("End of tape");
                                o.OnCompleted();
                            })
                            .Subscribe(_ => { }, e => o.OnError(e));
            });

            var isPaused = new BehaviorSubject<bool>(false);
            var setupOnPlayBack = Rxn.DfrCreate<IRxn>(o =>
            {
                var sincePlay = TimeSpan.Zero;
                var playbackBuffer = new Queue<ICapturedRxn>();

                var playbackQueue = sourceEventBuffer.Do(buffered =>
                {
                    playbackBuffer.Enqueue(buffered);
                })
                .Subscribe(_ => { }, () =>
                {
                    ReportStatus.Log.OnVerbose("Finished playbackQueue", nameof(UserAutomationPlayer));
                });

                var realTimeClock = Rxn.TimerWithPause(DateTimeOffset.MinValue, TimeSpan.FromSeconds(settings.TickSpeed), isPaused, _playbackScheduler)
                   .Skip(1) //so we can buffer the stream. need a better mechanism!?
                   .Do(tick =>
                   {
                       //pause
                       sincePlay = sincePlay.Add(TimeSpan.FromSeconds(settings.Speed));
                       CurrentThreadScheduler.Instance.Run(() => position.OnNext(sincePlay));

                       if (playbackBuffer.Count < 1)//todo: improve this when better buffreing mechanism implemented
                       {
                           o.OnCompleted();
                           return;
                       }
                       //finally play any events that have occoured based on the offset info
                       while (playbackBuffer.Count > 0 && playbackBuffer.Peek().Offset < sincePlay)
                           playBackStream.OnNext(playbackBuffer.Dequeue().Recorded);

                   })
                   .Subscribe(_ => { }, o.OnError, () =>
                   {
                       ReportStatus.Log.OnVerbose("Finished position", nameof(UserAutomationPlayer));
                   });

                return new CompositeDisposable(realTimeClock, playbackQueue, position, playBackStream.Subscribe(o), isPaused);
            });

            return new PlaybackStream(tape.Name, setupOnPlayBack, position, isPaused);
        }
    }
}
