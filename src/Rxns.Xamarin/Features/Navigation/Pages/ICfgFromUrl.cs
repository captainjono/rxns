namespace Rxns.Xamarin.Features.Navigation.Pages
{
    /// <summary>
    /// This the class that implements this should be seriliser friendly, allowing
    /// construction with no arguments.
    /// </summary>
    public interface ICfgFromUrl
    {
        /// <summary>
        /// Url encodes the implementing class so it can be sent over the wire
        /// </summary>
        /// <returns></returns>
        string UrlEncode();
        /// <summary>
        /// Creates an instance of the class from whatever is returned from UrlEncode()
        /// </summary>
        /// <param name="urlEncodedCfg"></param>
        /// <returns></returns>
        object FromUrl(string urlEncodedCfg);
    }
}
