using System;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Xamarin.Features.UserDomain
{
    public interface IAction : IRxn
    {
    }

    public interface IUserAction : IAction
    {
    }

    public class ShakeGesture : IUserAction
    {
    }

    public class ToggleIsLoading : IRxn
    {
        public string Message { get; private set; }

        public ToggleIsLoading(string message = null)
        {
            Message = message;
        }
    }

    public class ToastAlert : IUserAction
    {
        public string Title { get; private set; }
        public string Content { get; private set; }
        public LogLevel Level { get; private set; }
        public IRxn OnClick { get; private set; }
        public TimeSpan Duration { get; private set; }

        public ToastAlert() { }

        public ToastAlert(string title, string content, LogLevel level, TimeSpan? duration = null, IRxn onClick = null)
        {
            Title = title;
            Content = content;
            Level = level;
            OnClick = onClick;
            Duration = duration ?? TimeSpan.FromSeconds(5);
        }
    }

    public class DialogAlert : IUserAction
    {
        public string Title { get; private set; }
        public IRxn OnOk { get; private set; }
        public string Content { get; private set; }

        public DialogAlert() { }

        public DialogAlert(string content, string title = null, IRxn onOk = null)
        {
            Title = title;
            OnOk = onOk;
            Content = content;
        }
    }

    public class UserError : IUserAction
    {
        public string Title { get; private set; }
        public string Content { get; private set; }

        public UserError() { }
        public UserError(string content, string title = null)
        {
            Title = title;
            Content = content;
        }
    }

    public class AppError : IUserAction
    {
        public string Content { get; private set; }

        public AppError() { }
        public AppError(string content)
        {
            Content = content;
        }
    }

    public class UserQuestion : IUserAction
    {
        public string Title { get; private set; }
        public string Content { get; private set; }
        public IRxn Ok { get; private set; }
        public string OkText { get; private set; }
        public IRxn Cancel { get; private set; }
        public string CancelText { get; private set; }


        public UserQuestion(string content, IRxn onOk, IRxn onCancel, string title = "Please choose?")
        {
            Title = title;
            Content = content;
            Ok = onOk;
            Cancel = onCancel;
        }

        public UserQuestion(string title, string content, string okText, IRxn onOk, string cancelText, IRxn onCancel)
        {
            Title = title;
            Content = content;
            OkText = okText;
            Ok = onOk;
            CancelText = cancelText;
            Cancel = onCancel;
        }
    }

    public class UserAnswer : IUserAction, ICommandResult
    {
        public Guid InResponseTo { get; private set; }
        public CmdResult Result { get; private set; }
        public string Answer { get; private set; }

        public UserAnswer(UserInput question, string answer)
        {
            Answer = answer;
            Result = answer.IsNullOrWhitespace() ? CmdResult.Failure : CmdResult.Success;
            InResponseTo = question.Id;
        }
    }

    public class UserInput : IUserAction, IUniqueRxn
    {
        public Guid Id { get; private set; }

        public string Title { get; private set; }
        public string Content { get; private set; }

        public UserInput(string content, string title = "Please enter...")
        {
            Id = Guid.NewGuid();

            Title = title;
            Content = content;
        }
    }

    public class UserExecuted : IRxn
    {
        public string CommandPath { get; set; }
        public CP Parameter { get; set; }

        public UserExecuted()
        {
        }
    }

    public class CP
    {
        public object[] P { get; set; }

        public object PP { get { return P.Length > 0 ? P[0] : null; } }

        public CP(object param)
        {
            P = new object[] { param };
        }

        public CP() { }
    }

    public class UserLoggedIn : IUserAction
    {
        public string FriendlyName { get; private set; }

        public string UserName { get; private set; }

        public UserLoggedIn()
        {
        }

        public UserLoggedIn(string userName, string friendlyName)
        {
            this.UserName = userName;
            this.FriendlyName = friendlyName;
        }
    }

    public class UserLoggingOut : IUserAction { }
}
