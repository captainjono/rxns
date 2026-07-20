using System;
using System.Collections.Generic;
using System.Reactive.Threading.Tasks;
using Microsoft.Azure.NotificationHubs;

namespace Rxns.Azure
{
    public class AzurePushClient : IAzurePushClient
    {
        private readonly NotificationHubClient _client;

        public AzurePushClient(string connectionString, string notificationhubName)
        {
            _client = NotificationHubClient.CreateClientFromConnectionString(connectionString, notificationhubName);
        }

        public IObservable<IEnumerable<RegistrationDescription>> GetRegistrationsByTag(string tag, int top = 100)
        {
            return _client.GetRegistrationsByTagAsync(tag, top).ToObservable();
        }
            
        public IObservable<NotificationOutcome> SendAppleNativeNotification(string message, string tag)
        {
            return _client.SendAppleNativeNotificationAsync(message, tag).ToObservable();
        }

        public IObservable<NotificationOutcome> SendGcmNativeNotification(string message, string tag)
        {
            return _client.SendGcmNativeNotificationAsync(message, tag).ToObservable();
        }


        public IObservable<RegistrationDescription> UpdateRegistration(RegistrationDescription registration)
        {
            return _client.UpdateRegistrationAsync(registration).ToObservable();
        }


        public IObservable<RegistrationDescription> CreateAppleNativeRegistration(string deviceToken, params string[] tags)
        {
            return _client.CreateAppleNativeRegistrationAsync(deviceToken, tags).ToObservable();
        }


        public IObservable<RegistrationDescription> CreateGcmNativeRegistration(string registrationId, params string[] tags)
        {
            return _client.CreateGcmNativeRegistrationAsync(registrationId, tags).ToObservable();
        }
    }
}
