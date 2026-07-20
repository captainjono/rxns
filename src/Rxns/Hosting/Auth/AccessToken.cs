using System;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace Rxns.Hosting
{
    /// <summary>
    /// Represents a token that has been authorised to access the service
    /// </summary>
    public class AccessToken
    {
        /// <summary>
        /// The token that can be used for impersonating a user
        /// </summary>
        [JsonProperty("access_token")]
        public virtual string Token { get; set; }

        /// <summary>
        /// When the token expires
        /// </summary>
        [JsonProperty(".expires")]
        public DateTime Expires { get; set; }

        /// <summary>
        /// When the token was issued
        /// </summary>
        [JsonProperty(".issued")]
        public DateTime Issued { get; set; }

        public virtual string Scheme { get; set; }

        public AccessToken()
        {
            Scheme = "bearer";
        }

    }

    public static class AccessTokenExtensions
    {        
        public static AuthenticationHeaderValue ToAuthorizationHeader(this AccessToken token)
        {
            return new AuthenticationHeaderValue(token.Scheme, token.Token);
        }
    }
}
