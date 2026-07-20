using System;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    /// <summary>
    /// This service provides an mechanism to authentication with external entities
    /// and maintain the authentication effectively over the life of an applicaiton.
    /// It provides high level operations to take the pain out of authentication,
    /// as well as lower level operations for advanced authentication scenarios.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TCredentials"></typeparam>
    public interface IAuthenticationService<out T, TCredentials> : IReportStatus
    {
        TCredentials Credentials { get; set; }

        /// <summary>
        /// Indicates that the service should throw an error if SSL is not used
        /// to authenticate with. this is a safety mechanism because the OAuth scheme
        /// implemented by this authentication service is useless without SSL.
        /// </summary>
        bool RequiresSSL { get; set; }
        /// <summary>
        /// The stream of tokens that the service has received, with the latest token being the
        /// currently valid authentication ticket.
        /// </summary>
        IObservable<T> Tokens { get; }
        
        /// <summary>
        /// This sequence indicates the current state of the authentication service
        /// </summary>
        IObservable<bool> IsAuthenticated { get; }

        /// <summary>
        /// Tells the service that the current token is invalid and it should re-authenticate
        /// and get another token. The token is surfaced through the tokens observable
        /// </summary>
        IObservable<T> Refresh();

        /// <summary>
        /// Set the credentials used to authenticate with. This triggers
        /// an authentication request and if successful, a token will be surfaced through
        /// the returned observable AND the Tokens observable. If a token is already claimed and
        /// is still valid, the token will be returned and no login operations performed.
        /// </summary>
        IObservable<T> Login(TCredentials credentials);

        /// <summary>
        /// A low level operation that requests a token from the authentication service
        /// and returned it to the user without storing it or future requests.
        /// </summary>
        /// <param name="credentials"></param>
        /// <returns></returns>
        IObservable<T> GetToken(TCredentials credentials);
    }
}
