using System;
using System.Reactive.Linq;
using Rxns.Interfaces;

namespace Rxns.Commanding
{
    public interface ICommandResult
    {
        Guid InResponseTo { get; }
        CmdResult Result { get; }
    }

    public interface IUniqueRxn : IRxn
    {
        Guid Id { get; }
    }

    public class CommandResult : IRxn, ICommandResult
    {
        public CmdResult Result { get; private set; }
        public string Message;
        public Guid InResponseTo { get; private set; }

        public static CommandResult Success()
        {
            return new CommandResult()
            {
                Result = CmdResult.Success
            };
        }

        public static CommandResult Success(string message)
        {
            return new CommandResult()
            {
                Result = CmdResult.Success,
                Message = message
            };
        }

        public static CommandResult Failure(string message)
        {
            return new CommandResult()
            {
                Result = CmdResult.Failure,
                Message = message
            };
        }

        public static CommandResult Failure(string option, string message)
        {
            return new CommandResult()
            {
                Result = CmdResult.Failure,
                Message = String.Format("Invalid command: '{0}' - {1}", option, message)
            };
        }

        public CommandResult AsResultOf(IUniqueRxn cmd)
        {
            InResponseTo = cmd.Id;
            return this;
        }


    }

    public static class CommandResultExtensions
    {
        public static IObservable<CommandResult> ToCmdResult<T>(this IObservable<T> sequence)
        {
            return Observable.Create<CommandResult>(o =>
            {
                return sequence.Subscribe(r =>
                    {
                        o.OnNext(CommandResult.Success());
                        o.OnCompleted();
                    },
                    error =>
                    {
                        o.OnNext(CommandResult.Failure(error.Message));
                        o.OnCompleted();
                    });
            });
        }

    }

    public enum CmdResult
    {
        Success,
        Failure
    }
}
