using System;
using System.Collections.Generic;
using Microsoft.Azure.NotificationHubs;

namespace Rxns.Azure
{
    public interface IAzurePushClient
    {
        IObservable<IEnumerable<RegistrationDescription>> GetRegistrationsByTag(string tag, int top = 100);
        IObservable<NotificationOutcome> SendAppleNativeNotification(string message, string tag);
        IObservable<NotificationOutcome> SendGcmNativeNotification(string message, string tag);
        IObservable<RegistrationDescription> UpdateRegistration(RegistrationDescription registration);
        IObservable<RegistrationDescription> CreateAppleNativeRegistration(string deviceToken, params string[] tags);
        IObservable<RegistrationDescription> CreateGcmNativeRegistration(string registrationId, params string[] tags);
    }
}
