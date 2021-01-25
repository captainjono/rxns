using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Rxns.Hosting
{
    public class UserAccessToken : AccessToken
    {
        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty(".userName")]
        public string Username { get; set; }

        [JsonProperty(".role")]
        public string Role { get; set; }

        [JsonProperty(".isInternal")]
        public string IsInternal { get; set; }

        [JsonProperty(".name")]
        public string Name { get; set; }

        public override string ToString()
        {
            return String.Format("User<{0}>", Username);
        }
    }

    public interface IUserAuthenticationService
    {
        IObservable<bool> IsAuthenticated { get; }
        IObservable<UserAccessToken> Login(IUserCredentials credentials);
        IObservable<UserAccessToken> Tokens { get; }
        void Logout();
    }
}
