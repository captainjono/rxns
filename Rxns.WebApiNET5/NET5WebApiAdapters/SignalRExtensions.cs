using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace Microsoft.AspNet.SignalR.Client
{
    public static class SignalRExtensions
    {

        public static HubConnection ReportsWith(this HubConnection client, IReportStatus reporter, List<IDisposable> disposedBy)
        {
            var logger = new ReportsStatusTextWriter();
            logger.Information.Subscribe(info =>
            {
                switch (info.Level)
                {
                    case LogLevel.Verbose:
                        reporter.OnVerbose(info.Message);
                        break;
                    case LogLevel.Warning:
                        reporter.OnWarning(info.Message);
                        break;
                    case LogLevel.Error:
                        reporter.OnError(new Exception(info.Message));
                        break;
                    default:
                        reporter.OnInformation(info.Message);
                        break;
                }
            }).DisposedBy(disposedBy);

            logger.Errors.Subscribe(error => reporter.OnError(error.Message)).DisposedBy(disposedBy);

            client.TraceWriter = logger;
            client.TraceLevel = TraceLevels.All;

            return client;
        }

        /// <summary>
        /// This estblishes a link a between the client and an authentication service used
        /// to authenticate it. As tokens are obtained by the authentication service, it will
        /// updating the clients Authorization headers with new tokens to keep the clients
        /// credentials valid
        /// </summary>
        /// <param name="client"></param>
        /// <param name="authService"></param>
        /// <returns></returns>
        public static IObservable<Unit> WithAuthentication<T, TC>(this HubConnection client, IAuthenticationService<T, TC> authService) where  T : AccessToken
        {
            return Observable.Create<Unit>(o =>
            {
                return authService.Tokens.Subscribe(token =>
                {
                    client.Headers.AddOrReplace("Authorization", token.Token);
                    o.OnNext(new Unit());
                    o.OnCompleted();
                },
                error => o.OnError(error));
            });
        }
    }
}
