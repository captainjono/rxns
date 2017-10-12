using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Windows.Input;
using Rxns.Interfaces;
using Rxns.System.Collections.Generic;

namespace Rxns.Commanding
{
    public class RxnCommand : IRxnCommand
    {
        private readonly ICommand _cmd;
        private readonly IObservable<bool> _canExecute;
        private Func<object, bool> _canExecuteValue = _ => false;
        private readonly IDisposable _canExecuteSub = null;

        public RxnCommand(ICommand cmd, IObservable<bool> canExecute)
        {
            _cmd = cmd;
            _canExecute = canExecute;

            _canExecuteSub = _canExecute.Catch(Observable.Return(false)).Subscribe(v =>
            {
                _canExecuteValue = _ => v;
                ChangeCanExecute();
            });
        }

        public RxnCommand(ICommand cmd, Func<object, bool> canExecute)
        {
            _cmd = cmd;
            _canExecuteValue = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecuteValue(parameter);
        }

        public void Execute(object parameter)
        {
            _cmd.Execute(parameter);
        }

        public void ChangeCanExecute()
        {
            var eventHandler = this.CanExecuteChanged;
            if (eventHandler == null) return;

            eventHandler((object)this, EventArgs.Empty);
        }

        public event EventHandler CanExecuteChanged;
        public void Dispose()
        {
            if (_canExecuteSub != null)
                _canExecuteSub.Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onExecute">The action to run when the command is executed</param>
        /// <param name="canExecute">Whether or not the command is enabled. Null defaults to always enabled</param>
        /// <returns></returns>
        public static IRxnCommand Create(Action onExecute, IObservable<bool> canExecute = null)
        {
            return new RxnCommand(onExecute.ToCommand(), canExecute ?? Observable.Return(true));
        }

        public static IRxnCommand Create(Action<object> onExecute, IObservable<bool> canExecute = null)
        {
            return new RxnCommand(onExecute.ToCommand(), canExecute ?? Observable.Return(true));
        }

        public static IRxnCommand Create(Action onExecute, Func<object, bool> canExecute)
        {
            return new RxnCommand(onExecute.ToCommand(), canExecute);
        }

        public static IRxnCommand Create(Action<object> onExecute, Func<object, bool> canExecute)
        {
            return new RxnCommand(onExecute.ToCommand(), canExecute);
        }
    }

    public static class RxnCommandExtensions
    {
        public static Func<Action, ICommand> ComamndImpl = action => { throw new NotImplementedException("You need to specify a CommandImpl for your platform on startup"); };
        public static Func<Action<object>, ICommand> ComamndParamImpl = (param) => { throw new NotImplementedException("You need to specify a CommandImpl for your platform on startup"); };

        /// <summary>
        /// The factory function used to create commands
        /// </summary>
        /// <param name="onExecute">The action to execute when the command is run</param>
        /// <returns>A new command as specified by CommandImpl</returns>
        public static ICommand ToCommand(this Action onExecute)
        {
            return ComamndImpl(onExecute);
        }

        public static ICommand ToCommand(this Action<object> onExecute)
        {
            return ComamndParamImpl(onExecute);
        }

        /// <summary>
        /// Creates a new command which pumps events into a dispatcher in a slim and efficent way on the current thread
        /// </summary>
        /// <typeparam name="TReturnedEvent">The base type of rxn the dispatcher receives</typeparam>
        /// <param name="publish">The dispatcher</param>
        /// <param name="value">The rxn to pump when the command is run</param>
        /// <param name="canExecute">A stream of values that indicate if the command can be executed or not</param>
        /// <returns></returns>
        public static IRxnCommand OnExecute<TReturnedEvent>(this Action<IRxn> publish, Func<object, TReturnedEvent> value, IObservable<bool> canExecute = null) where TReturnedEvent : IRxn
        {
            return new RxnCommand(new Action<object>(forP => publish(value(forP))).ToCommand(), canExecute ?? Observable.Return(true));
        }

        public static IRxnCommand OnExecute<TReturnedEvent>(this Action<IRxn> publish, TReturnedEvent value, IObservable<bool> canExecute = null) where TReturnedEvent : IRxn
        {
            return new RxnCommand(new Action<object>(forP => publish(value)).ToCommand(), canExecute ?? Observable.Return(true));
        }

        public static IRxnCommand OnExecute<TReturnedEvent>(this Action<IRxn> publish, IEnumerable<TReturnedEvent> values, IObservable<bool> canExecute = null) where TReturnedEvent : IRxn
        {
            return new RxnCommand(new Action<object>(forP => values.ForEach(value => publish(value))).ToCommand(), canExecute ?? Observable.Return(true));
        }

        public static IRxnCommand OnExecute<TReturnedEvent>(this Action<IRxn> publish, Func<object, IEnumerable<TReturnedEvent>> values, IObservable<bool> canExecute = null) where TReturnedEvent : IRxn
        {
            return new RxnCommand(new Action<object>(forP => values(forP).ForEach(value => publish(value))).ToCommand(), canExecute ?? Observable.Return(true));
        }
        
        public static IRxnCommand OnExecute<TCmdParam, TReturnedEvent>(this Action<IRxn> publish, Func<TCmdParam, TReturnedEvent> value, IObservable<bool> canExecute = null) where TReturnedEvent : IRxn
        {
            return new RxnCommand(new Action<object>(forP => publish(value((TCmdParam)forP))).ToCommand(), canExecute ?? Observable.Return(true));
        }
    }
}
