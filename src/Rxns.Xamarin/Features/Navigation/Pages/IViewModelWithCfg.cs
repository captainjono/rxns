namespace Rxns.Xamarin.Features.Navigation.Pages
{
    /// <summary>
    /// A view model which can *optionally* be configured
    /// All values should be nullable and the VM should handle situations where
    /// configuration is not supplied
    /// </summary>
    /// <typeparam name="TCfg">The type of configuration object the viewModel uses</typeparam>
    public interface IViewModelWithCfg<in TCfg> : IViewModel where TCfg : ICfgFromUrl
    {
        void Configure(TCfg cfg);
    }
}
