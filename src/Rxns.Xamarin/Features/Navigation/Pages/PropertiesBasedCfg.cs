using System;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    /// <summary>
    /// This class uses the objects public properties to create a string
    /// representation of the class to be serilised using reflaction
    /// 
    /// NOTE: Always specify a default parameterless constructor!
    /// </summary>
    public class PropertiesBasedCfg : ICfgFromUrl
    {
        public string UrlEncode()
        {
            throw new NotImplementedException();
        }

        public object FromUrl(string urlEncodedCfg)
        {
            throw new NotImplementedException();
        }
    }
}
