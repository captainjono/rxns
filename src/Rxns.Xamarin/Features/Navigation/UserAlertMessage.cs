using Rxns.Xamarin.Features.UserDomain;

namespace Rxns.Xamarin.Features.Navigation
{
    public class UserAlertMessage : IAction
    {
        public string Title { get; private set; }
        public string Message { get; private set; }

        public UserAlertMessage(string title, string message)
        {
            Title = title;
            Message = message;
        }
    }
}
