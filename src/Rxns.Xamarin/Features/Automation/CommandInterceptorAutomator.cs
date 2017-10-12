using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Rxns.Interfaces;
using Rxns.Xamarin.Features.Automation.PlayBackFilter;
using Rxns.Xamarin.Features.Navigation.Pages;
using Rxns.Xamarin.Features.UserDomain;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Automation
{
    /// <summary>
    /// This automator swaps out commands in the Page for a UserExecuted action, which in tern is used to execute the actual command.
    /// This is more of an interceptor in its style, as it doesnt actually "tap the screen" to verify that the UI element is not hidden by
    /// something or has a click-able area. It does respect the CanExecute command property
    /// 
    /// How does it match commands with the controls that plug them?
    /// The commands are matched on the model variable they bound too (by reference) and the CommandParameter that the control
    /// that executes them has.
    /// 
    /// Limitations
    /// - commands on same page need to be unique command/parameter combination
    /// - params need to be unique with toString()
    ///
    /// </summary>
    public class CommandInterceptorAutomator : IAutomateUserActions
    {
        private readonly IUserAlerts _dialog;

        public class InterceptState
        {
            public readonly List<object> Searched = new List<object>();
            public readonly Dictionary<string, int> Commands = new Dictionary<string, int>();
            public readonly Action<IRxn> Publish;
            public readonly IObservable<IRxn> Input;
            public readonly RxnPageModel Model;

            public InterceptState(RxnPageModel model, IObservable<IRxn> input, Action<IRxn> publish)
            {
                Model = model;
                Input = input;
                Publish = publish;
            }
        }

        public ITapePlaybackFilter[] Filters { get; private set; }
        public CommandInterceptorAutomator(ITapePlaybackFilter[] filters, IUserAlerts dialog)
        {
            _dialog = dialog;
            Filters = filters.Concat(new ITapePlaybackFilter[]
            {
                new CommandInterceptorPlaybackFilter(), //should always be the last filter
            }).ToArray();
        }

        public IObservable<bool> AutomateUserActions(Page page, RxnPageModel model, IObservable<IRxn> actions, Action<IRxn> publish)
        {
            return RxObservable.DfrCreate<bool>(o =>
            {
                var state = new InterceptState(model, actions, publish);
                var resources = new CompositeDisposable(new DisposableAction(() => { Debug.WriteLine("<<<<Disposing of {0}>>>>>", model.GetType().Name); }));

                if (page is ContentPage)
                {
                    Observable.FromEventPattern(e => page.LayoutChanged += e, e => page.LayoutChanged -= e)
                            .Select(_ => new Unit())
                            .StartWith(new Unit())
                            .BufferFirstLast(TimeSpan.FromMilliseconds(70), false)
                            .Do(_ => o.OnNext(false))
                            .Do(_ => _dialog.ShowLoading("Loading..."))
                            .Do(_ => Debug.WriteLine("Intercepting>>> {0}", model.GetType().Name))

                            .Select(_ =>
                            {
                                return Observable.Defer(() => RxObservable.Create(() =>
                                {
                                    var timer = Stopwatch.StartNew();
                                    var r = FindAllCommandHolder(((ContentPage)page).Content as IViewContainer<View>, state);
                                    timer.Stop();
                                    Debug.WriteLine("Interception took: {0}", timer.Elapsed);

                                    if (r != null) resources.Add(r);
                                }));
                            })
                            .Switch()
                            .Do(_ => _dialog.HideLoading())
                            .FinallyR(() => _dialog.HideLoading())
                            .Subscribe(_ => o.OnNext(true))
                            .DisposedBy(resources);

                    return resources;
                };

                return Disposable.Empty;
            });
        }

        private IDisposable FindAllCommandHolder(IViewContainer<View> content, InterceptState state)
        {
            if (content == null) return null;

            return FindAllCommandHolder(content.Children.ToArray(), state);
        }

        private IDisposable FindAllCommandHolder(ILayoutController content, InterceptState state)
        {
            if (content == null) return null;

            return FindAllCommandHolder(content.Children.ToArray(), state);
        }

        private IDisposable FindAllCommandHolder(Element[] content, InterceptState state)
        {
            var resources = new CompositeDisposable();
            if (content == null) return null;

            foreach (var child in content)
            {
                //if (state.Searched.Contains(child)) continue;
                //state.Searched.Add(child);

                var container = child as IViewContainer<View>;
                if (container != null)
                {
                    var r1 = FindAllCommandHolder(container, state);
                    if (r1 != null) resources.Add(r1);
                }

                var container1 = child as ILayoutController;
                if (container1 != null)
                {
                    var r1 = FindAllCommandHolder(container1, state);
                    if (r1 != null) resources.Add(r1);
                }

                var r = InterceptCommand(child, state);
                if (r != null) resources.Add(r);

            }

            return resources.Count > 0 ? resources : null;
        }



        ///the main goal here is that i want to make it 
        /// -simple to create UI test
        /// -refactor safe tests, move a command to a different control, the command still works
        /// -support a mode where the taps are executed on the screen, and the resulting command is paired with it
        /// -the interceptor will only activate in UI record mode, and it will generate an extra event, and "UserTapped PropertyName/CommandParameter (serilised)"
        /// -playback will see this usertapped command, search on the page for the first matching combo, and then tap the screen over it and wit for a response to match with the existing recorded response
        private IDisposable InterceptCommand(Element child, InterceptState state)
        {
            var resources = new CompositeDisposable();
            var commands = CommandOn(child);
            foreach (var hasCommand in commands)
            {
                //Debug.WriteLine("Looking at {0} => {1}", hasCommand.Item1, hasCommand.Item2);
                if (state.Searched.Contains(hasCommand.Item1)) continue;
                state.Searched.Add(hasCommand.Item1);

                var value = hasCommand.Item1.GetProperty(hasCommand.Item2);
                if (value != null)
                {
                    var propertyNameOfCommand = PropertyNameOn(state.Model, value) ?? PropertyNameOn(child.BindingContext, value);
                    if (propertyNameOfCommand != null)
                    {
                        var originalCmd = hasCommand.Item1.GetProperty(hasCommand.Item2) as ICommand;
                        var propertyNameOfCmdParameter = "{0}Parameter".FormatWith(hasCommand.Item2);
                        var hasParam = hasCommand.Item1.HasProperty(propertyNameOfCmdParameter);
                        Func<object> getCmdParameter = () => hasParam ? hasCommand.Item1.GetProperty(propertyNameOfCmdParameter) : null;
                        var cmdParameter = getCmdParameter();

                        //two problems currently
                        // - activityInfo references icons filesource, should switch to string binding to image because as cmdParam it has IdGuid
                        // - the pages that load async are causing the commands not to be hooked up. implement IObservable<Unit> SHowBackground and make
                        // - isLoading a vm property. then use this as a sync point, pause the video while pages load? think thats  good idea too
                        var code = GetCode(propertyNameOfCommand, cmdParameter);
                        if (state.Commands.ContainsKey(code))
                            state.Commands[code]++;
                        else
                            state.Commands.Add(code, 0);
                        var id = state.Commands[code];
                        propertyNameOfCommand = "{0}%{1}".FormatWith(propertyNameOfCommand, id);


                        RxnApp.UIScheduler.Run(() =>
                            hasCommand.Item1.GetPropertyDef(hasCommand.Item2).SetValue(hasCommand.Item1, new Command(p =>
                            {
                                try
                                {
                                    //incase its changed or whoever is implementing ICommand is 
                                    //dynamically geneerating a parameter on click, lets update
                                    cmdParameter = p;
                                    state.Publish(new UserExecuted()
                                    {
                                        CommandPath = propertyNameOfCommand,
                                        Parameter = new CP(cmdParameter)
                                    });
                                }
                                catch (Exception e)
                                {
                                    Debug.WriteLine(e);
                                }
                            })));

                        state.Input.OfType<UserExecuted>()
                            .Where(w =>
                            {
                                return !hasParam ? w.CommandPath == propertyNameOfCommand : w.CommandPath == propertyNameOfCommand && w.Parameter.PP == cmdParameter;
                            })
                            .Where(ue => originalCmd.CanExecute(ue.Parameter.PP)) //incase of null params, where params are dependent on user input, use the stored value
                            .Do(ue => originalCmd.Execute(ue.Parameter.PP))
                            .Until()
                            .DisposedBy(resources);

                        new DisposableAction(() => hasCommand.Item1.GetPropertyDef(hasCommand.Item2).SetValue(hasCommand.Item1, originalCmd)).DisposedBy(resources);

                        Debug.WriteLine("<Intercepted> [{0}]{1} ->{2}", hasCommand.Item1.GetHashCode(), hasCommand.Item1.GetType().Name, propertyNameOfCommand);

                    }
                }
            }

            return resources.Count > 0 ? resources : null;
        }

        private string GetCode(string propertyNameOfCommand, object cmdParameter)
        {
            return "{0}%{1}".FormatWith(propertyNameOfCommand ?? "", cmdParameter ?? "");
        }

        private Tuple<object, string>[] CommandOn(object context)
        {
            if (context == null) return new Tuple<object, string>[] { };
            var allCommands = new List<Tuple<object, string>>();
            var propertys = context.GetType().GetProperties().Where(p => p.PropertyType.IsInterface() || p.PropertyType.IsByRef || p.PropertyType.IsArray);

            foreach (var property in propertys.ToArray())
            {

                if (property.PropertyType == typeof(ICommand))
                    allCommands.Add(new Tuple<object, string>(context, property.Name));
                else if (property.PropertyType.IsArray || property.PropertyType.IsAssignableTo<IEnumerable>())
                {
                    //Debug.WriteLine("Looking at {0} -> {1}", context.GetType().Name, property.Name);

                    var group = context.GetProperty(property.Name) as IEnumerable;
                    if (group != null)
                        foreach (var prop in group)
                            allCommands.AddRange(CommandOn(prop));
                }
            }

            return allCommands.ToArray();
        }

        private string PropertyNameOn(object context, object value)
        {
            var allCommands = context.GetType().GetProperties().Where(p => p.PropertyType == typeof(ICommand));
            var property = allCommands.FirstOrDefault(c => context.GetProperty(c.Name) == value);

            return property != null ? property.Name : SearchDeepForPropertyName(context, value);
        }

        private string SearchDeepForPropertyName(object context, object value)
        {
            var toSearch = context.GetType().GetProperties().Where(p => p.PropertyType.IsByRef);

            foreach (var obj in toSearch)
            {
                var result = PropertyNameOn(obj, value);
                if (result != null) return result;
            }

            return null;
        }
    }
}
