using System;
using System.Reactive.Linq;
using Rxns.Logging;

namespace Rxns.Microservices
{
    /// <summary>
    /// Represents an object that needs to be passed around, with an action taken 
    /// against it once its delivery is complete and is no longer needed
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ReliabilityTicket<T> : IDisposable
    {
        public T Content { get; set; }
        private Action OnDisposed { get; set; }

        public static ReliabilityTicket<T> Create(T content, Action confirmsContent)
        {
            var ticket = new ReliabilityTicket<T>
            {
                Content = content,
                OnDisposed = confirmsContent
            };

            return ticket;
        }

        public void Dispose()
        {
            OnDisposed();
        }
    }

    public interface IConfirmationManager<T>
    {
        void Confirm(ReliabilityTicket<T> eventToConfirm);
    }

    public class AutoConfirmingManager<T> : ReportsStatus, IConfirmationManager<T>
    {
        public void Confirm(ReliabilityTicket<T> toConfirm)
        {
            this.ReportExceptions(() =>
            {
                toConfirm.Dispose();
            }, error => OnError("Could not confirm event: {0}\r\n{1}", toConfirm.Content.ToString().Replace("{", "[").Replace("}", "]"), error));
        }
    }
}
