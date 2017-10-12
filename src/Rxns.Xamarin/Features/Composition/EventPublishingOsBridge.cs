using System;
using Rxns.Interfaces;

namespace Rxns.Xamarin.Features.Composition
{
    /// <summary>
    /// An OS bridge which publishes action events when OS actions occour
    /// </summary>
    public class EventPublishingOsBridge : IAppToOsBridge, IRxnPublisher<IRxn>
    {
        public class AppResumed : IRxn { }
        public class AppBackgrounded : IRxn { }

        private Action<IRxn> _publish;

        public void ConfigiurePublishFunc(Action<IRxn> eventFunc)
        {
            _publish = eventFunc;
        }

        /// <summary>
        /// Publishes an AppResumed
        /// </summary>
        public void OnResume()
        {
            _publish(new AppResumed());
        }

        /// <summary>
        /// Publishes AppBackgrounded
        /// </summary>
        public void OnBackground()
        {
            _publish(new AppBackgrounded());
        }
    }
}
