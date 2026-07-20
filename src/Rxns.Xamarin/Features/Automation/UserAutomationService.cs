using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;
using Rxns.Xamarin.Features.Composition;
using Rxns.Xamarin.Features.Navigation;
using Rxns.Xamarin.Features.Navigation.Pages;
using Rxns.Xamarin.Features.UserDomain;
using Xamarin.Forms;
using ITapeRepository = Rxns.Xamarin.Features.Automation.ITapeRepository;

namespace Rxns.Xamarin.Features.Automation
{
    /// <summary>
    /// start is being called twice, need to work out why the VM is being double hooked up? think thats the problem! or this rxn is being double hooked up?!
    /// </summary>
    public class UserAutomationService : ReportStatusService,
                                         IRxnProcessor<UserAutomationService.StartStopRecording>,
                                         IRxnProcessor<UserAutomationService.PlayRecording>,
                                         IRxnProcessor<UserAutomationService.DeleteRecording>,
                                         IReactTo<IRxn>
    {
        public interface INotRecorded : IRxn { }
        private readonly ITapeRecorder _recorder;
        private readonly ITapeRepository _tapeRepository;
        private readonly INavigationService<IRxnPageModel> _nav;
        private readonly IRxAppNav<Page, RxnPageModel> _appNav;
        private readonly CompositeDisposable _recording;
        private ITapeStuff _tape;
        private readonly Action<IRxn> _publish;
        private readonly IAutomateUserActions _automator;

        public class StartStopRecording : INotRecorded
        {
            public string TapeName { get; set; }

            public StartStopRecording(string name)
            {
                TapeName = name;
            }
        }

        public class StartRecording : UserInput, INotRecorded
        {
            public StartRecording(string content, string title = "Recording name...")
                : base(content, title)
            {
            }
        }

        public class PlayRecording : INotRecorded
        {
            public string Name { get; private set; }

            public PlayRecording(string name)
            {
                Name = name;
            }
        }

        public class DeleteRecording : INotRecorded
        {
            public string Name { get; private set; }

            public DeleteRecording(string name)
            {
                Name = name;
            }
        }

        public ISubject<IRxn> Input { get; set; }
        public ISubject<IRxn> Output { get; set; }


        public UserAutomationService(ITapeRecorder recorder, ITapeRepository tapeRepository, INavigationService<IRxnPageModel> nav, IRxAppNav<Page, RxnPageModel> appNav, IAutomateUserActions automator)
        {
            _recorder = recorder;
            _tapeRepository = tapeRepository;
            _nav = nav;
            _appNav = appNav;
            _automator = automator;
            Input = new Subject<IRxn>();
            Output = new Subject<IRxn>();
            _publish = e => Output.OnNext(e);

            _recording = new CompositeDisposable();
        }

        public IObservable<ITapeStuff[]> GetAll()
        {
            return _tapeRepository.GetAll();
        }

        public IObservable<IRxn> Process(StartStopRecording @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                if (_tape != null)
                {
                    OnVerbose("Tape {0} goes for {1}".FormatWith(_tape.Name, _tape.Source.Duration));

                    _recording.Dispose();
                    _recording.Clear();
                    _tape.Source.Dispose();
                    _tape = null;
                    return;
                }

                _tape = _tapeRepository.GetOrCreate(@event.TapeName);
                _appNav.CurrentView.Select(view => _automator.AutomateUserActions(view.Page, view.Model, Input, Input.OnNext))
                                                            .Switch()
                                                            .Until(OnError)
                                                            .DisposedBy(_recording);

                _recorder.Record(_tape, Input.Where(e => !e.GetType().ImplementsInterface<INotRecorded>())).DisposedBy(_recording);
                OnVerbose("Recording {0} has started".FormatWith(@event.TapeName));
            });
        }

        public IObservable<IRxn> Process(PlayRecording @event)
        {
            return Rxn.Create<IRxn>(o =>
            {
                //todo: fix way inital page of recording is shown so playback is consistant
                o.OnCompleted();

                return Disposable.Empty;
            })
            .Do(__ =>
            {
                CurrentThreadScheduler.Instance.Run(() =>
                {
                    var tape = _tapeRepository.GetOrCreate("{0}".FormatWith(@event.Name));
                    var player = _recorder.Play(tape, new PlaybackSettings() { Speed = 1, TickSpeed = 1 });

                    OnVerbose("Playing {0} @ {1}", tape.Name, tape.Source.Duration);
                    var stopPositionDebug = player.Position.Do(p => OnVerbose("Current: {0}", p)).Until(OnError);

                    //generate the UI automation events only while playing
                    var automator = _appNav.CurrentView
                                                .Do(_ => OnWarning("New Page {0}", _.Model.GetType().Name))
                                                .Select(view => _automator.AutomateUserActions(view.Page, view.Model, Input, _publish).Do(isReady => player.IsPaused.OnNext(!isReady)))
                                                .Switch()
                                                .Until(OnError);

                   var pageTransition = this.OnReactionTo<NavigationAction>()
                                            .Do(_ => OnWarning("Pausing"))
                                            .Do(_ => player.IsPaused.OnNext(true))
                                            .Until(OnError);

                    var recording = player.Stream.Select(e => FilterPlayback(e))
                                                 .Where(e => e != null)
                                                 .FinallyR(() =>
                                                 {
                                                     automator.Dispose();
                                                     pageTransition.Dispose();
                                                 })
                                                 .Do(_ => OnWarning("PLAYING>> {0}-{1}", _.GetType().Name, _.Serialise()))
                                                 .Subscribe(p => _publish(p), error => OnError(error));
                });
            });
        }

        private IRxn _lastEvent = null;
        private IRxn FilterPlayback(IRxn e)
        {
            //when a user presses the back button in the nav bar or other mechanism to pop
            //a page without a navigation action, we need to handle this!
            if (e is PhoneNavigationOrchestrator.PoppedPage)
            {
                var alreadyPopped = _lastEvent as NavigationAction;
                if (alreadyPopped == null || alreadyPopped.IsPushing)
                    return _nav.Pop();
            }

            _lastEvent = e;
            Debug.WriteLine("Saw: {0}", e.GetType());

            foreach (var filter in _automator.Filters)
            {
                e = filter.FilterPlayback(e);
                if (e == null) return null;
            }

            Debug.WriteLine("Playing");

            return e;
        }

        public IObservable<IRxn> Process(DeleteRecording @event)
        {
            return Rxn.Create<IRxn>(() => _tapeRepository.Delete(@event.Name));
        }
    }
}
