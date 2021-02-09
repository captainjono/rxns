namespace Rxns.Hosting
{
    public interface IWebApiCfg
    {
        string Html5Root { get; }
        string Html5IndexHtml { get; }
        string Port { get; }
        string BindingUrl { get; }
    }

    public class WebApiCfg : IWebApiCfg
    {
        public string Html5Root { get; set; }
        public string Html5IndexHtml { get; set; }
        public string Port { get; set; }
        public string BindingUrl { get; set; }
    }

}
