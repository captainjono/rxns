namespace Rxns.Hosting
{
    public interface IWebApiCfg
    {
        string Html5Root { get; }
        string Html5IndexHtml { get; }
        int Port { get; }
        string BindingUrl { get; }
    }

    public static class WebApiCfgExtensions
    {
        public static string LocalUrl(this IWebApiCfg cfg) => "http://localhost:" + cfg.Port;

        public static string ResolveBindingUrl(this IWebApiCfg cfg)
            => string.IsNullOrWhiteSpace(cfg.BindingUrl) ? "http://*:" + cfg.Port : cfg.BindingUrl;
    }

    /// <summary>
    /// Optional companion to <see cref="IWebApiCfg"/> for hosts that ship an
    /// augment overlay. <c>AspNetCoreWebApiAdapter</c> casts the cfg to this
    /// interface; null/empty/missing <see cref="AugmentRoot"/> = no overlay
    /// mounted, base SPA behaves identically to a non-augmented host. The
    /// overlay is served at <c>/augment/*</c> and the base SPA's index.html
    /// loads <c>/augment/init.js</c> with <c>onerror=this.remove()</c>.
    /// </summary>
    public interface IAugmentableCfg
    {
        string AugmentRoot { get; }
    }

    public class WebApiCfg : IWebApiCfg
    {
        public string Html5Root { get; set; }
        public string Html5IndexHtml { get; set; }
        public int Port { get; set; } = 888;
        public string BindingUrl { get; set; }
    }

}
